using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscRippa.Core.Music;

/// <summary>Understands what cyanrip prints and builds its command lines.</summary>
public static class Cyanrip
{
    #region Fields

    private static readonly Regex KeyValue = new(@"^\s*([A-Za-z][A-Za-z0-9 _\-]*?)\s*[:=]\s*(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex TrackHeader = new(@"^\s*Track\s*(?:#|no\.?|number)?\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Percent = new(@"(\d{1,3}(?:\.\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex TrackWord = new(@"\btrack\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    //"Ripping and encoding track 4, progress - 37.50%, ETA - 1h 2m 5s"
    private static readonly Regex Eta = new(@"ETA\s*-\s*((?:\d+\s*[hms]\s*)+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EtaPart = new(@"(\d+)\s*([hms])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ErrorCount = new(@"^\s*Ripping errors:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrackDone = new(@"^\s*Track\s+(\d+)\s+ripped and encoded successfully", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    #endregion

    #region Methods

    /// <summary>
    /// Reads the album/track details from `cyanrip -I` output. Tolerant on purpose: "key: value" lines before any
    /// track belong to the album; after a "Track N" line they belong to that track.
    /// </summary>
    public static CdMetadata ParseInfo(IEnumerable<string> lines)
    {
        var meta = new CdMetadata();
        int? track = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r', '\n');
            var header = TrackHeader.Match(line);
            if (header.Success)
            {
                track = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                //"Track 3: Title" style
                var rest = line.Substring(header.Length).TrimStart(' ', ':', '-', '.');
                if (rest.Length > 0 && !rest.Contains(':') && !meta.TrackTitles.ContainsKey(track.Value))
                    meta.TrackTitles[track.Value] = rest.Trim();
                continue;
            }
            var kv = KeyValue.Match(line);
            if (!kv.Success) continue;
            var key = kv.Groups[1].Value.Trim().ToLowerInvariant().Replace(' ', '_');
            var value = kv.Groups[2].Value.Trim();
            if (value.Length == 0) continue;

            if (track == null)
            {
                switch (key)
                {
                    case "album": case "album_title": meta.Album = value; break;
                    case "album_artist": case "albumartist": meta.AlbumArtist = value; break;
                    case "artist": if (meta.AlbumArtist.Length == 0) meta.AlbumArtist = value; break;
                    case "date": case "year": case "release_date": meta.Date = value; break;
                    case "device_model": meta.DriveModel = value; break;
                    case "disc_number": case "disc": if (int.TryParse(value, out var d) && d > 0) meta.Disc = d; break;
                    case "total_discs": case "totaldiscs": if (int.TryParse(value, out var td) && td > 0) meta.TotalDiscs = td; break;
                }
            }
            else
            {
                switch (key)
                {
                    case "title": meta.TrackTitles[track.Value] = value; break;
                    case "artist": meta.TrackArtists[track.Value] = value; break;
                    //cyanrip prints the album's tags again under every track; use them if the album lines lacked them
                    case "date": if (meta.Date.Length == 0) meta.Date = value; break;
                    case "album": if (meta.Album.Length == 0) meta.Album = value; break;
                    case "album_artist": if (meta.AlbumArtist.Length == 0) meta.AlbumArtist = value; break;
                    case "discname": if (meta.DiscName.Length == 0) meta.DiscName = value; break;
                    case "disc": if (int.TryParse(value, out var d2) && d2 > 0) meta.Disc = d2; break;
                    case "totaldiscs": if (int.TryParse(value, out var t2) && t2 > 0) meta.TotalDiscs = t2; break;
                }
            }
        }
        if (meta.Found) meta.Source = MetadataSource.MusicBrainz;
        return meta;
    }

    /// <summary>(track number or null, percent or null) from one line of cyanrip's progress output.</summary>
    /// <summary>cyanrip's own estimate for the current track ("ETA - 1h 2m 5s"), or null.</summary>
    public static TimeSpan? ParseEta(string line)
    {
        var m = Eta.Match(line);
        if (!m.Success) return null;
        double seconds = 0;
        foreach (Match p in EtaPart.Matches(m.Groups[1].Value))
        {
            var n = double.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
            seconds += char.ToLowerInvariant(p.Groups[2].Value[0]) switch { 'h' => n * 3600, 'm' => n * 60, _ => n };
        }
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>The "Ripping errors: N" line cyanrip prints at the end, or null.</summary>
    public static int? ParseErrorCount(string line)
    {
        var m = ErrorCount.Match(line);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>"Track 4 ripped and encoded successfully!" -> 4.</summary>
    public static int? ParseTrackDone(string line)
    {
        var m = TrackDone.Match(line);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>True for cyanrip's progress lines, which it prints many times a second.</summary>
    public static bool IsProgressLine(string line) => line.Contains("progress -", StringComparison.OrdinalIgnoreCase);

    /// <summary>The track and percent from a progress line, where it has them.</summary>
    public static (int? Track, double? Percent) ParseProgress(string line)
    {
        if (!IsProgressLine(line))
        {
            var finished = ParseTrackDone(line);
            return (finished, finished != null ? 100.0 : (double?)null);
        }
        int? track = null;
        double? pct = null;
        var t = TrackWord.Match(line);
        if (t.Success) track = int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture);
        var p = Percent.Match(line);
        if (p.Success && double.TryParse(p.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v <= 100)
            pct = v;
        return (track, pct);
    }

    /// <summary>
    /// Lines that mean something went wrong while ripping. Lookup notes such as "Unable to get cover art "back": not found!"
    /// or "Accurip: not found" are normal for many CDs and don't count.
    /// </summary>
    public static bool LooksLikeProblem(string line)
    {
        if (Regex.IsMatch(line, @"cover art|accu ?rip|musicbrainz|not found|missing entry|ripping errors:|c2 errors", RegexOptions.IgnoreCase)) return false;
        if (IsProgressLine(line)) return false;
        return Regex.IsMatch(line, @"(error|fail|unable|could not|couldn't|mismatch|readerr|offset is unset)", RegexOptions.IgnoreCase);
    }

    /// <summary>cyanrip exits with 0 but rips nothing when the drive's offset hasn't been given.</summary>
    public static bool IsOffsetUnset(string line) => line.Contains("Offset is unset", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The read offset for a drive model, when known. LG (HL-DT-ST) Blu-ray and DVD drives are almost all +6.
    /// Null = unknown (the app then uses 0 and says so).
    /// </summary>
    public static int? KnownOffset(string driveModel)
        => driveModel.Contains("HL-DT-ST", StringComparison.OrdinalIgnoreCase) ? 6 : null;

    /// <summary>`cyanrip -I -d G:`: print the disc's details without ripping.</summary>
    public static string[] InfoArgs(string drive) => new[] { "-I", "-d", drive };

    /// <summary>
    /// The rip, with plain ASCII arguments only (see <see cref="CdRipSession"/>): FLAC, no online lookup (-N, the app
    /// writes the names itself), into <paramref name="workFolder"/> (relative to the working directory) as 01.flac, 02.flac…
    /// </summary>
    /// <remarks>
    /// -K turns off cyanrip's ReplayGain. With it on, cyanrip writes no file at all until every track has been read, so
    /// stopping part-way would keep nothing; with -K each track is saved as soon as it's ripped.
    /// </remarks>
    public static List<string> RipArgs(string drive, string workFolder, IReadOnlyCollection<int> tracks, int offset = 0)
    {
        var args = new List<string>
        {
            "-d", drive, "-s", offset.ToString(CultureInfo.InvariantCulture), "-o", "flac", "-N",
            "-D", workFolder, "-F", "{track}", "-L", "rip", "-M", "rip", "-K",
        };
        if (tracks.Count > 0)
        {
            args.Add("-l");
            args.Add(string.Join(",", tracks.OrderBy(n => n)));
        }
        return args;
    }

    /// <summary>Music\Artist\Album for the chosen names.</summary>
    public static string AlbumFolder(string musicRoot, string artist, string album, int disc = 1, int totalDiscs = 1)
    {
        var folder = Path.Combine(musicRoot,
                                  NoBraces(FileNames.Safe(artist.Trim().Length > 0 ? artist : "Unknown Artist")),
                                  NoBraces(FileNames.Safe(album.Trim().Length > 0 ? album : "Unknown Album")));
        //a box set: each disc gets its own folder so their "01 - …" files can't collide.
        return totalDiscs > 1 ? Path.Combine(folder, $"Disc {Math.Max(1, disc)}") : folder;
    }

    /// <summary>cyanrip treats {…} in folder names as placeholders, so names can't contain braces.</summary>
    public static string NoBraces(string s) => s.Replace('{', '(').Replace('}', ')');

    #endregion
}
