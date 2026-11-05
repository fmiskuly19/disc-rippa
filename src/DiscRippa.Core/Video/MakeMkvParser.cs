using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace DiscRippa.Core.Video;

/// <summary>Understands the "robot mode" text that makemkvcon prints.</summary>
public static class MakeMkvParser
{
    #region Fields

    //messages that mean "something went wrong while reading or saving".
    private static readonly Regex ErrorRegex = new(
        @"(read error|scsi error|medium error|unrecovered|error\W.{0,80}occurred|failed to|" +
        @"hash check failed|corrupt|damaged|bad sector|i/o error|has expired|too old)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CopyRegex = new(
        @"Copy complete\.\s*(\d+) titles? saved(?:, (\d+) failed)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProgressRegex = new(@"^PRGV:(\d+),(\d+),(\d+)", RegexOptions.Compiled);
    private static readonly Regex ProgressTitleRegex = new("^PRG([TC]):\\d+,\\d+,\"(.*)\"\\s*$", RegexOptions.Compiled);
    private static readonly Regex TitleLineRegex = new("^TINFO:(\\d+),(\\d+),\\d+,\"(.*)\"\\s*$", RegexOptions.Compiled);
    private static readonly Regex StreamLineRegex = new("^SINFO:(\\d+),(\\d+),(\\d+),\\d+,\"(.*)\"\\s*$", RegexOptions.Compiled);
    private static readonly Regex DiscLineRegex = new("^CINFO:(\\d+),\\d+,\"(.*)\"\\s*$", RegexOptions.Compiled);

    //the "saving" stage of a rip, as opposed to opening/scanning the disc first.
    private static readonly Regex SavingStageRegex = new(@"sav|mkv|writ", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    #endregion

    #region Methods

    /// <summary>Splits one comma separated line, honouring "quoted, text" and \" escapes.</summary>
    public static List<string> SplitCsv(string s)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < s.Length && (s[i + 1] == '"' || s[i + 1] == '\\'))
                {
                    sb.Append(s[++i]);
                }
                else if (c == '"')
                {
                    if (i + 1 < s.Length && s[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }

    /// <summary>MakeMKV's escaped quotes as plain quotes.</summary>
    private static string Unescape(string value) => value.Replace("\\\"", "\"");

    /// <summary>The readable text of a "MSG:" line, or null for any other line.</summary>
    public static string? ParseMessage(string line)
    {
        if (!line.StartsWith("MSG:", StringComparison.Ordinal)) return null;
        var fields = SplitCsv(line.Substring(4).TrimEnd('\r', '\n'));
        return fields.Count > 3 ? fields[3] : null;
    }

    /// <summary>Progress (0-100) of the whole current stage, from a "PRGV:" line, or null.</summary>
    public static double? ParseProgress(string line) => ParseProgressValues(line)?.Total;

    /// <summary>(current step %, whole stage %) from "PRGV:current,total,max", or null.</summary>
    public static (double Current, double Total)? ParseProgressValues(string line)
    {
        var m = ProgressRegex.Match(line);
        if (!m.Success) return null;
        double max = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (max <= 0) return null;
        double current = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        double total = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        return (Math.Clamp(current / max * 100.0, 0, 100), Math.Clamp(total / max * 100.0, 0, 100));
    }

    /// <summary>The stage ("PRGT") or step ("PRGC") MakeMKV has just started, or null.</summary>
    public static ProgressTitle? ParseProgressTitle(string line)
    {
        var m = ProgressTitleRegex.Match(line);
        if (!m.Success) return null;
        return new ProgressTitle(m.Groups[1].Value == "T", Unescape(m.Groups[2].Value));
    }

    /// <summary>True for the stage that actually writes the MKV (MakeMKV first opens and scans the disc).</summary>
    public static bool IsSavingStage(string stageName) => SavingStageRegex.IsMatch(stageName);

    /// <summary>True if a MakeMKV message reads like an error.</summary>
    public static bool LooksLikeError(string message) => ErrorRegex.IsMatch(message);

    /// <summary>(saved, failed) from "Copy complete. 1 titles saved, 1 failed." or null.</summary>
    public static (int Saved, int Failed)? ParseCopyComplete(string message)
    {
        var m = CopyRegex.Match(message);
        if (!m.Success) return null;
        int saved = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        int failed = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        return (saved, failed);
    }

    /// <summary>
    /// The drive's name without its firmware version and serial number, which MakeMKV adds to the end:
    /// "BD-RE HL-DT-ST BD-RE  BP50NB40 1.01 AB1C2D3E45678" -> "BD-RE HL-DT-ST BD-RE BP50NB40".
    /// </summary>
    public static string CleanDriveName(string name)
    {
        var cleaned = Regex.Replace(name.Trim(), @"\s+\d+(\.\d+)+(\s+\S+)?$", "");
        return Regex.Replace(cleaned, @"\s{2,}", " ");
    }

    /// <summary>The first drive that has a readable disc in it, from "DRV:" lines.</summary>
    public static DiscDrive? FindDisc(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("DRV:", StringComparison.Ordinal)) continue;
            var f = SplitCsv(line.Substring(4).TrimEnd('\r', '\n'));
            if (f.Count < 6) continue;
            if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) continue;
            if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int state)) continue;
            var disc = f[5];
            var device = f.Count > 6 ? f[6] : "";
            if (state == 2 && disc.Length > 0) return new DiscDrive(index, disc, device, CleanDriveName(f[4])); //2 = disc inserted and readable
        }
        return null;
    }

    /// <summary>Every disc drive MakeMKV can see (empty slots are skipped), from "DRV:" lines.</summary>
    public static List<DriveStatus> ParseDrives(IEnumerable<string> lines)
    {
        var drives = new List<DriveStatus>();
        foreach (var line in lines)
        {
            if (!line.StartsWith("DRV:", StringComparison.Ordinal)) continue;
            var f = SplitCsv(line.Substring(4).TrimEnd('\r', '\n'));
            if (f.Count < 6) continue;
            if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) continue;
            if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int state)) continue;
            if (state >= 256 || f[4].Trim().Length == 0) continue;   //256 = no drive in this slot
            drives.Add(new DriveStatus(index, state, CleanDriveName(f[4]), f[5], f.Count > 6 ? f[6] : ""));
        }
        return drives;
    }

    /// <summary>Disc type and name, from "CINFO:" lines.</summary>
    public static DiscInfo ParseDiscInfo(IEnumerable<string> lines)
    {
        var info = new DiscInfo();
        foreach (var raw in lines)
        {
            var m = DiscLineRegex.Match(raw.TrimEnd('\r', '\n'));
            if (!m.Success) continue;
            var value = Unescape(m.Groups[2].Value);
            switch (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            {
                case 1: info.Type = value; break;
                case 2: info.Name = value; break;
                case 32: info.VolumeName = value; break;
            }
        }
        return info;
    }

    /// <summary>Every title on the disc with its streams, from "TINFO:" and "SINFO:" lines.</summary>
    public static List<TitleInfo> ParseTitles(IEnumerable<string> lines)
    {
        var titles = new SortedDictionary<int, TitleInfo>();
        var streams = new Dictionary<(int, int), StreamInfo>();

        TitleInfo Title(int id)
        {
            if (!titles.TryGetValue(id, out var t))
            {
                t = new TitleInfo { Id = id };
                titles[id] = t;
            }
            return t;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r', '\n');

            var m = TitleLineRegex.Match(line);
            if (m.Success)
            {
                var t = Title(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                var value = Unescape(m.Groups[3].Value);
                switch (int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
                {
                    case 2: t.Name = value; break;
                    case 8: t.Chapters = ToInt(value); break;
                    case 9:
                        t.Duration = value;
                        var parts = value.Split(':').Select(ToInt).ToList();
                        while (parts.Count < 3) parts.Insert(0, 0);
                        t.Seconds = parts[0] * 3600 + parts[1] * 60 + parts[2];
                        break;
                    case 10: t.SizeText = value; break;
                    case 11: if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) t.Size = size; break;
                    case 16: t.SourceFile = value; break;
                    case 27: t.OutputFileName = value; break;
                }
                continue;
            }

            var s = StreamLineRegex.Match(line);
            if (s.Success)
            {
                int titleId = int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture);
                int streamId = int.Parse(s.Groups[2].Value, CultureInfo.InvariantCulture);
                var t = Title(titleId);
                if (!streams.TryGetValue((titleId, streamId), out var st))
                {
                    st = new StreamInfo { Index = streamId };
                    streams[(titleId, streamId)] = st;
                    t.Streams.Add(st);
                }
                var value = Unescape(s.Groups[4].Value);
                switch (int.Parse(s.Groups[3].Value, CultureInfo.InvariantCulture))
                {
                    case 1: st.Type = value; break;
                    case 3: st.LangCode = value; break;
                    case 4: st.LangName = value; break;
                    case 6: st.CodecShort = value; break;
                    case 7: st.CodecLong = value; break;
                    case 14: st.Channels = ToInt(value); break;
                    case 19: st.VideoSize = value; break;
                    case 20: st.AspectRatio = value; break;
                    case 21: st.FrameRate = value; break;
                    case 30: st.Description = value; break;
                    case 40: st.ChannelLayout = value; break;
                }
            }
        }

        foreach (var t in titles.Values) t.Streams.Sort((a, b) => a.Index.CompareTo(b.Index));
        return titles.Values.ToList();
    }

    /// <summary>The number, or 0.</summary>
    private static int ToInt(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    #endregion
}
