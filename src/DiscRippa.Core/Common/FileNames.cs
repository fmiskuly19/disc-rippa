using System.Globalization;
using System.Text.Json;
using System.Text;

namespace DiscRippa.Core.Common;

public static class FileNames
{
    #region Methods

    /// <summary>"Movie.mkv" -> "Movie (2).mkv" if it already exists, and so on.</summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int k = 2; ; k++)
        {
            var candidate = Path.Combine(dir, $"{name} ({k}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Removes characters Windows doesn't allow in file names.</summary>
    public static string Safe(string name)
    {
        const string bad = "<>:\"/\\|?*";
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (c >= ' ' && bad.IndexOf(c) < 0) sb.Append(c);
        }
        var cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        return cleaned.Length == 0 ? "Movie" : cleaned;
    }

    /// <summary>"THE_MOVIE_NAME" -> "The Movie Name" (labels on discs are usually shouted).</summary>
    public static string CleanDiscName(string raw)
    {
        var name = raw.Replace('_', ' ').Trim();
        bool hasLetter = false;
        foreach (var c in name) { if (char.IsLetter(c))
        {
            hasLetter = true;
            break;
        } }
        if (hasLetter && name == name.ToUpperInvariant())
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        return name;
    }

    /// <summary>Deletes a file if it is there; never throws. Returns true if it is gone afterwards.</summary>
    public static bool TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return !File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
