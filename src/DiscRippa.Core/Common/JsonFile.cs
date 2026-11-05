using System.Globalization;
using System.Text.Json;
using System.Text;

namespace DiscRippa.Core.Common;

public static class JsonFile
{
    #region Fields

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    #endregion

    #region Methods

    /// <summary>Writes via a temp file so a crash can't leave half a file behind.</summary>
    public static void Save<T>(string path, T value)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, true);
        }
        catch
        {
            //not being able to save settings must never break ripping.
        }
    }

    #endregion

    #region Nested types

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
