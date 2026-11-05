using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace DiscRippa.Core.Compression;

/// <summary>The presets HandBrake offers, and which file type (mp4 / mkv / mov) each one produces.</summary>
public sealed class PresetCatalog
{
    #region Fields

    private static readonly Regex ContainerRegex = new(
        @"in\s+an?\s+(MP4|MKV|MOV)\s+container", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, string> _extensions;

    #endregion

    #region Properties

    public static PresetCatalog Empty => new(Array.Empty<string>(), new Dictionary<string, string>());
    public IReadOnlyList<string> Names { get; }
    public bool HasPresets => Names.Count > 0;

    #endregion

    #region Constructors

    /// <summary>Creates a catalog of HandBrake presets and the file extension each one makes.</summary>
    public PresetCatalog(IReadOnlyList<string> names, Dictionary<string, string> extensions)
    {
        Names = names;
        _extensions = extensions;
    }

    #endregion

    #region Methods

    /// <summary>".mp4", ".mkv" or ".mov" for this preset, including a sensible guess for unknown names.</summary>
    public string ExtensionFor(string preset)
        => _extensions.TryGetValue(preset, out var ext) ? ext : HandBrake.FallbackExtension(preset);

    /// <summary>Reads the output of <c>HandBrakeCLI --preset-list</c>.</summary>
    public static PresetCatalog Parse(string text)
    {
        var names = new List<string>();
        var descriptions = new Dictionary<string, StringBuilder>();
        string category = "";
        string? current = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ', '\t');
            if (line.Trim().Length == 0) continue;

            if (line[0] != ' ')
            {
                //"General/" starts a category; anything else unindented (prompts, "HandBrake has exited.") is noise.
                if (line.EndsWith("/", StringComparison.Ordinal))
                {
                    category = line.Substring(0, line.Length - 1);
                    current = null;
                }
                continue;
            }
            if (category.Length == 0) continue;

            int indent = line.Length - line.TrimStart(' ').Length;
            if (indent <= 5)
            {
                //a preset name
                var name = line.Trim();
                if (name == "CLI Default")
                {
                    current = null;
                    continue;
                }
                current = name;
                if (!descriptions.ContainsKey(name))
                {
                    names.Add(name);
                    descriptions[name] = new StringBuilder();
                }
            }
            else if (current != null)
            {
                //its (wrapped) description
                descriptions[current].Append(' ').Append(line.Trim());
            }
        }

        var extensions = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var m = ContainerRegex.Match(descriptions[name].ToString());
            extensions[name] = "." + (m.Success ? m.Groups[1].Value.ToLowerInvariant() : "mp4");
        }
        return new PresetCatalog(names, extensions);
    }

    #endregion
}
