using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace DiscRippa.Core.Compression;

public static class HandBrake
{
    #region Fields

    private static readonly Regex SizeRegex = new(@"size:\s*(\d+)x(\d+)", RegexOptions.Compiled);

    private static readonly Regex ProgressRegex = new(
        @"Encoding:\s*task\s+(\d+)\s+of\s+(\d+),\s*(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);

    private static readonly Regex EtaRegex = new(@"ETA\s+(\d+)h(\d+)m(\d+)s", RegexOptions.Compiled);
    private static readonly Regex HardwareEncoderRegex = new(@"\b(nvenc|qsv|vce|vcn|mf)_h265\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    #endregion

    #region Properties

    /// <summary>The graphics-card encoders HandBrake supports, best first: (key, preset name part, what to call it).</summary>
    private static readonly (string Key, string PresetPart, string Name)[] HardwareFamilies =
    {
        ("nvenc", "NVENC", "NVIDIA NVENC"),
        ("qsv", "QSV", "Intel Quick Sync"),
        ("vcn", "VCN", "AMD VCN"),
        ("mf", "MF", "Windows Media Foundation"),
    };

    /// <summary>
    /// Audio for the graphics-card presets, which on their own keep only a stereo track: the same two tracks the HQ
    /// Surround presets make from the main soundtrack. A stereo AAC track for phones and TVs, plus the 5.1 surround
    /// track, passed through untouched when it's already AC-3, otherwise converted to 5.1 AC-3 at 640 kbps.
    /// </summary>
    public static readonly string[] SurroundAudioArgs =
    {
        "--audio", "1,1",
        "--aencoder", "av_aac,copy:ac3",
        "--ab", "160,640",
        "--mixdown", "stereo,5point1",
        "--audio-fallback", "ac3",
    };

    /// <summary>
    /// For compressed copies saved as MKV: every audio track copied exactly as it is on the disc (TrueHD, DTS-HD MA,
    /// Atmos, commentaries, other languages), every subtitle kept, and the file written as MKV. Only a track MKV can't hold
    /// as-is falls back to AC-3.
    /// </summary>
    public static readonly string[] KeepAllTracksArgs =
    {
        "--format", "av_mkv",
        "--audio-lang-list", "any", "--all-audio",
        "--aencoder", "copy",
        "--audio-copy-mask", "aac,ac3,eac3,truehd,dts,dtshd,mp3,flac,opus",
        "--audio-fallback", "ac3",
        "--subtitle-lang-list", "any", "--all-subtitles",
    };

    #endregion

    #region Methods

    /// <summary>The file type a preset makes when HandBrake hasn't told us.</summary>
    public static string FallbackExtension(string preset)
    {
        var p = preset.ToLowerInvariant();
        if (p.Contains("mkv") || p.StartsWith("preservation", StringComparison.Ordinal)) return ".mkv";
        if (p.Contains("dnxhr") || p.Contains("prores")) return ".mov";
        return ".mp4";
    }

    /// <summary>
    /// Picks the "HQ ... Surround" preset that matches a source of this size. It never scales up, and it
    /// looks at the width too because widescreen films are often cropped (a 1920x800 rip still needs 1080p).
    /// </summary>
    public static string ChooseAutoPreset(int height, IReadOnlyCollection<string>? available, int? width = null)
    {
        int w = width ?? 0;
        string want;
        bool big;
        if (w >= 3000 || height >= 1500)
        {
            want = "HQ 2160p60 4K HEVC Surround";
            big = true;
        }
        else if (w >= 1700 || height >= 1000)
        {
            want = "HQ 1080p30 Surround";
            big = true;
        }
        else if (w >= 1100 || height >= 650)
        {
            want = "HQ 720p30 Surround";
            big = false;
        }
        else if (height >= 540)
        {
            want = "HQ 576p25 Surround";
            big = false;
        }
        else
        {
            want = "HQ 480p30 Surround";
            big = false;
        }

        if (available == null || available.Count == 0 || available.Contains(want)) return want;
        return big ? "Fast 1080p30" : "Fast 480p30";
    }

    /// <summary>
    /// Which graphics-card H.265 encoders HandBrake can use on this PC, from <c>HandBrakeCLI --help</c>: its encoder list
    /// only names the ones it found working hardware for. Returns keys like "nvenc", "qsv", "vcn".
    /// </summary>
    public static IReadOnlyCollection<string> ParseHardwareEncoders(string helpText)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in HardwareEncoderRegex.Matches(helpText ?? ""))
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            found.Add(key == "vce" ? "vcn" : key);   //AMD's encoder is called vce_* but its presets say VCN
        }
        return HardwareFamilies.Select(f => f.Key).Where(found.Contains).ToList();
    }

    /// <summary>
    /// The graphics-card preset for this resolution ("H.265 NVENC 1080p", or the 2160p one for 4K), using the best
    /// encoder this PC has; null when there isn't one. These presets never upscale, so DVDs keep their own size.
    /// </summary>
    public static (string Preset, string Name)? ChooseHardwarePreset(int height, int width, IReadOnlyCollection<string> hardware,
                                                                  IReadOnlyCollection<string>? available)
    {
        bool uhd = width >= 3000 || height >= 1500;
        foreach (var (key, part, name) in HardwareFamilies)
        {
            if (!hardware.Contains(key)) continue;
            var preset = uhd ? $"H.265 {part} 2160p 4K" : $"H.265 {part} 1080p";
            if (available == null || available.Count == 0 || available.Contains(preset)) return (preset, name);
        }
        return null;
    }

    /// <summary>"NVIDIA NVENC" etc. for the best encoder found, or null.</summary>
    public static string? HardwareName(IReadOnlyCollection<string> hardware)
        => HardwareFamilies.Where(f => hardware.Contains(f.Key)).Select(f => f.Name).FirstOrDefault();

    /// <summary>(width, height) of the first title in <c>HandBrakeCLI -t 0</c> output, or null.</summary>
    public static (int Width, int Height)? ParseSourceSize(string scanOutput)
    {
        var m = SizeRegex.Match(scanOutput);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>Reads "Encoding: task 1 of 1, 45.12 % (... ETA 00h12m34s)". Returns overall percent and ETA text.</summary>
    public static bool TryParseProgress(string line, out double percent, out string eta)
    {
        percent = 0;
        eta = "";
        var m = ProgressRegex.Match(line);
        if (!m.Success) return false;

        int task = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int tasks = Math.Max(1, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        double pct = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        percent = Math.Clamp(((task - 1) + pct / 100.0) / tasks * 100.0, 0, 100);

        var e = EtaRegex.Match(line);
        if (e.Success)
            eta = $"{int.Parse(e.Groups[1].Value, CultureInfo.InvariantCulture)}:{e.Groups[2].Value}:{e.Groups[3].Value}";
        return true;
    }

    /// <summary>The command as you would type it, for error messages.</summary>
    public static string FormatCommand(string exe, IEnumerable<string> args)
        => string.Join(" ", new[] { exe }.Concat(args).Select(a => a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a));

    #endregion
}
