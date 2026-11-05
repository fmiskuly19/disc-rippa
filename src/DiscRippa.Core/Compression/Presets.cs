using System.Globalization;
using System.Text.Json;
using System.Text;

namespace DiscRippa.Core.Compression;

public static class Presets
{
    #region Fields

    /// <summary>Shown first in every quality list: pick the preset from the file's own resolution.</summary>
    public const string Auto = "Auto (match the source)";

    #endregion

    #region Properties

    /// <summary>Used until HandBrake has told us which presets it really has.</summary>
    public static readonly string[] Fallback =
    {
        "Fast 1080p30",
        "HQ 1080p30 Surround",
        "Super HQ 1080p30 Surround",
        "Fast 720p30",
        "Fast 480p30",
    };

    #endregion
}
