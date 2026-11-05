using System.Collections.ObjectModel;

namespace DiscRippa.Core.Music;

/// <summary>
/// Reads album details and rips a music CD to FLAC with cyanrip.
/// The Windows build of cyanrip garbles anything but plain ASCII in its arguments and keeps quote marks literally, so it
/// is only given plain arguments: it rips into a scratch folder as 01.flac, 02.flac… with no names. The app then checks
/// every file is real FLAC, writes the tags itself (any characters work) and renames it to "01 - Title.flac".
/// </summary>
public sealed class CdRipSession
{
    #region Fields

    public const string WorkFolderPrefix = "_discrippa-";
    private readonly IProcessRunner _runner;
    private readonly Func<string?> _cyanripPath;

    #endregion

    #region Properties

    /// <summary>cyanrip's estimate for the track being ripped.</summary>
    public TimeSpan? TrackEta { get; private set; }

    /// <summary>Every line cyanrip printed during the last call (for the log file).</summary>
    public List<string> Log { get; } = new();

    #endregion

    #region Constructors

    /// <summary>Creates a session that runs the cyanrip at <paramref name="cyanripPath"/>.</summary>
    public CdRipSession(IProcessRunner runner, Func<string?> cyanripPath)
    {
        _runner = runner;
        _cyanripPath = cyanripPath;
    }

    #endregion

    #region Methods

    /// <summary>The album and track names from MusicBrainz (via `cyanrip -I`). Empty if not found or no cyanrip.</summary>
    public async Task<CdMetadata> LookUpAsync(string drive, CancellationToken ct, Action<string>? onLine = null)
    {
        var exe = _cyanripPath();
        if (string.IsNullOrEmpty(exe)) return new CdMetadata();
        var lines = new List<string>();
        var outcome = await _runner.RunAsync(exe, Cyanrip.InfoArgs(drive), l =>
        {
            lines.Add(l);
            onLine?.Invoke(l);
        }, ct).ConfigureAwait(true);
        Log.Clear();
        Log.AddRange(lines);
        if (outcome.Cancelled || outcome.StartFailed) return new CdMetadata();
        return Cyanrip.ParseInfo(lines);
    }

    /// <summary>Rips the chosen tracks, then checks, tags and names every file.</summary>
    public async Task<CdRipResult> RunAsync(CdRipRequest request, Action<CdRipProgress>? onProgress, CancellationToken ct,
                                            Action<string>? onMessage = null)
    {
        Log.Clear();
        var exe = _cyanripPath();
        if (string.IsNullOrEmpty(exe))
            return new CdRipResult { Error = "cyanrip wasn't found." };
        if (request.Tracks.Count == 0) return new CdRipResult { Error = "No tracks were selected." };

        var folder = Path.GetFullPath(request.Folder);
        var workName = WorkFolderPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var work = Path.Combine(folder, workName);
        //remember which folders this rip creates, so a rip that saves nothing doesn't leave empty folders behind.
        var created = new List<string>();
        for (var d = folder; !string.IsNullOrEmpty(d) && !Directory.Exists(d); d = Path.GetDirectoryName(d)) created.Add(d);
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            return new CdRipResult { Error = $"Couldn't create the folder {folder}: {ex.Message}" };
        }

        var order = request.Tracks.OrderBy(n => n).ToList();
        int problems = 0;
        int index = 0;
        double trackPct = 0;
        var done = new HashSet<int>();

        void Report()
        {
            var overall = Math.Clamp((index + trackPct / 100.0) / order.Count * 100.0, 0, 100);
            onProgress?.Invoke(new CdRipProgress(index + 1, order.Count, order[Math.Min(index, order.Count - 1)], trackPct, overall));
        }

        Report();
        var args = Cyanrip.RipArgs(request.Drive, workName, order, request.Offset);
        var outcome = await _runner.RunInAsync(folder, exe, args, line =>
        {
            //cyanrip prints its progress line thousands of times per track; the log and screen get the rest.
            if (!Cyanrip.IsProgressLine(line))
            {
                Log.Add(line);
                onMessage?.Invoke(line);
            }
            if (Cyanrip.LooksLikeProblem(line)) problems++;
            if (Cyanrip.ParseErrorCount(line) is int reported && reported > problems) problems = reported;
            if (Cyanrip.ParseEta(line) is TimeSpan eta) TrackEta = eta;
            if (Cyanrip.ParseTrackDone(line) is int finished) done.Add(finished);
            var (track, pct) = Cyanrip.ParseProgress(line);
            if (track != null)
            {
                int i = order.IndexOf(track.Value);
                if (i > index)
                {
                    index = i;
                    trackPct = 0;
                }
            }
            if (pct != null && pct.Value >= trackPct) trackPct = pct.Value;
            Report();
        }, ct).ConfigureAwait(true);

        bool cancelled = outcome.Cancelled || ct.IsCancellationRequested;

        var produced = new Dictionary<int, string>();
        try
        {
            if (Directory.Exists(work))
                foreach (var f in Directory.GetFiles(work, "*.flac"))
                    if (LeadingNumber(Path.GetFileName(f)) is int n) produced[n] = f;
        }
        catch
        {
        }

        var files = new List<string>();
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   //"01.flac" -> "01 - Title.flac"
        var broken = new List<int>();
        string? tagError = null;
        foreach (var n in order)
        {
            if (!produced.TryGetValue(n, out var path)) continue;
            if (cancelled && !done.Contains(n))
            {
                FileNames.TryDelete(path);
                continue;
            }   //the unfinished track
            if (Flac.Inspect(path) == null)
            {
                broken.Add(n);
                Log.Add($"Track {n}: the FLAC file cyanrip wrote is empty or damaged ({new FileInfo(path).Length} bytes); it was removed.");
                FileNames.TryDelete(path);
                continue;
            }
            try
            {
                Flac.WriteTags(path, TagsFor(n, request));
            }
            catch (Exception ex)
            {
                tagError ??= $"Couldn't write the names into track {n}: {ex.Message}";
            }

            var dest = UniqueFile(Path.Combine(folder, TrackFileName(n, request.Metadata)));
            try
            {
                File.Move(path, dest);
                renamed[Path.GetFileName(path)] = Path.GetFileName(dest);
                files.Add(Path.GetFileName(dest));
            }
            catch (Exception ex)
            {
                tagError ??= $"Couldn't rename track {n}: {ex.Message}";
                files.Add(Path.Combine(workName, Path.GetFileName(path)));
            }
        }
        //the cue sheet and log describe the whole rip, so they're only kept when every track made it. After an abort or a
        //failure they'd be half-finished (cyanrip's log is still kept as the app's own "last CD rip" log).
        bool complete = !cancelled && !outcome.StartFailed && outcome.ExitCode == 0 && broken.Count == 0 && tagError == null
                        && files.Count > 0 && order.All(produced.ContainsKey);
        if (complete)
            MoveCueAndLog(work, folder, request.Metadata, renamed, order);
        else
        {
            FileNames.TryDelete(Path.Combine(work, "rip.cue"));
            FileNames.TryDelete(Path.Combine(work, "rip.log"));
        }
        try
        {
            if (Directory.Exists(work) && !Directory.EnumerateFileSystemEntries(work).Any()) Directory.Delete(work);
        }
        catch
        {
        }
        foreach (var d in created)   //deepest first
        {
            try
            {
                if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                else break;
            }
            catch
            {
                break;
            }
        }
        if (tagError != null) Log.Add(tagError);

        if (cancelled)
            return new CdRipResult { Cancelled = true, Files = files, Problems = problems, Error = "Aborted." };
        if (outcome.StartFailed)
            return new CdRipResult { Error = $"cyanrip couldn't be started: {outcome.Error}" };
        if (outcome.ExitCode != 0)
        {
            var last = Log.LastOrDefault(l => Cyanrip.LooksLikeProblem(l)) ?? Log.LastOrDefault(l => l.Trim().Length > 0) ?? "";
            return new CdRipResult { Error = $"cyanrip stopped with an error (code {outcome.ExitCode}). {last}".Trim(), Files = files, Problems = problems };
        }
        if (files.Count == 0 && broken.Count == 0 && Log.Any(Cyanrip.IsOffsetUnset))
            return new CdRipResult { Error = "cyanrip wants the CD drive's read offset. Set it in Settings (LG / HL-DT-ST drives are usually +6).", Problems = problems };
        if (broken.Count > 0)
            return new CdRipResult
            {
                Error = $"cyanrip wrote empty or damaged files for {Plural(broken.Count, "track")} ({string.Join(", ", broken)}), so they were removed. " +
                        (files.Count > 0 ? $"The other {Plural(files.Count, "track")} are fine." : "No track was saved."),
                Files = files, Problems = problems + broken.Count,
            };
        var missing = order.Where(n => !produced.ContainsKey(n)).ToList();
        if (files.Count == 0)
            return new CdRipResult { Error = "cyanrip finished but no FLAC file appeared in " + folder, Problems = problems };
        if (missing.Count > 0)
            return new CdRipResult { Error = $"cyanrip didn't produce {Plural(missing.Count, "track")}: {string.Join(", ", missing)}.", Files = files, Problems = problems };
        if (tagError != null)
            return new CdRipResult { Error = tagError, Files = files, Problems = problems };

        trackPct = 100; index = order.Count - 1; Report();
        return new CdRipResult { Success = true, Files = files, Problems = problems };
    }

    /// <summary>"01 - Fan the Fire.flac" (the same name the CD screen shows).</summary>
    public static string TrackFileName(int number, CdMetadata meta)
    {
        var title = meta.TrackTitles.TryGetValue(number, out var t) && t.Trim().Length > 0 ? t.Trim() : $"Track {number:00}";
        return $"{number:00} - {FileNames.Safe(title)}.flac";
    }

    /// <summary>The tags written into each file. Empty values remove cyanrip's own versions of the same tags.</summary>
    public static Dictionary<string, string> TagsFor(int number, CdRipRequest request)
    {
        var m = request.Metadata;
        var artist = m.TrackArtists.TryGetValue(number, out var a) && a.Trim().Length > 0 ? a : m.AlbumArtist;
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TITLE"] = m.TrackTitles.TryGetValue(number, out var t) && t.Trim().Length > 0 ? t : $"Track {number:00}",
            ["ARTIST"] = artist,
            ["ALBUM"] = m.Album,
            ["ALBUMARTIST"] = m.AlbumArtist,
            ["ALBUM_ARTIST"] = "",
            ["DATE"] = m.Date,
            ["TRACKNUMBER"] = number.ToString(),
            ["TRACK"] = "",
            ["TRACKTOTAL"] = Math.Max(request.TotalTracks, request.Tracks.DefaultIfEmpty(number).Max()).ToString(),
            ["TOTALTRACKS"] = "",
            ["DISCNUMBER"] = m.TotalDiscs > 1 ? m.Disc.ToString() : "",
            ["DISC"] = "",
            ["DISCTOTAL"] = m.TotalDiscs > 1 ? m.TotalDiscs.ToString() : "",
            ["TOTALDISCS"] = "",
            ["DISCSUBTITLE"] = m.TotalDiscs > 1 ? m.DiscName : "",
        };
        return tags;
    }

    /// <summary>
    /// cyanrip's cue sheet and log are named "rip": they become "Album.cue" / "Album.log" next to the tracks, and the cue
    /// sheet is pointed at the renamed files, with the album and track names added.
    /// </summary>
    private static void MoveCueAndLog(string work, string folder, CdMetadata meta, Dictionary<string, string> renamed, List<int> order)
    {
        var baseName = FileNames.Safe(meta.Album.Trim().Length > 0 ? meta.Album : "Album");
        if (meta.TotalDiscs > 1) baseName += $" (disc {meta.Disc})";
        try
        {
            var cue = Path.Combine(work, "rip.cue");
            if (File.Exists(cue))
            {
                var lines = new List<string>();
                if (meta.AlbumArtist.Length > 0) lines.Add($"PERFORMER \"{CueText(meta.AlbumArtist)}\"");
                if (meta.Album.Length > 0) lines.Add($"TITLE \"{CueText(meta.Album)}\"");
                foreach (var raw in File.ReadAllLines(cue))
                {
                    var line = raw;
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("FILE \"", StringComparison.Ordinal))
                    {
                        int q = trimmed.IndexOf('"', 6);
                        var name = q > 6 ? Path.GetFileName(trimmed.Substring(6, q - 6)) : "";
                        if (renamed.TryGetValue(name, out var newName)) line = $"FILE \"{newName}\"{trimmed.Substring(q + 1)}";
                    }
                    else if ((trimmed.StartsWith("TITLE ", StringComparison.Ordinal) || trimmed.StartsWith("PERFORMER ", StringComparison.Ordinal))
                             && !line.StartsWith(" ", StringComparison.Ordinal))
                    {
                        continue;   //album-level lines are written above
                    }
                    lines.Add(line);
                    var m = System.Text.RegularExpressions.Regex.Match(trimmed, @"^TRACK\s+(\d+)\s+AUDIO");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
                    {
                        if (meta.TrackTitles.TryGetValue(n, out var title) && title.Length > 0) lines.Add($"    TITLE \"{CueText(title)}\"");
                        var performer = meta.TrackArtists.TryGetValue(n, out var a) && a.Length > 0 ? a : meta.AlbumArtist;
                        if (performer.Length > 0) lines.Add($"    PERFORMER \"{CueText(performer)}\"");
                    }
                }
                File.WriteAllLines(UniqueFile(Path.Combine(folder, baseName + ".cue")), lines, new System.Text.UTF8Encoding(true));
                FileNames.TryDelete(cue);
            }
        }
        catch
        {
        }
        try
        {
            var log = Path.Combine(work, "rip.log");
            if (File.Exists(log)) File.Move(log, UniqueFile(Path.Combine(folder, baseName + ".log")));
        }
        catch
        {
        }
    }

    /// <summary>Text made safe for a cue sheet (no double quotes).</summary>
    private static string CueText(string s) => s.Replace('"', '\'');

    /// <summary>The path, or "Name (2).ext" and so on if it is taken.</summary>
    private static string UniqueFile(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int k = 2; ; k++)
        {
            var candidate = Path.Combine(dir, $"{name} ({k}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>"1 track" or "3 tracks".</summary>
    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    /// <summary>"04.flac" or "04 - Interlude.flac" -> 4.</summary>
    private static int? LeadingNumber(string file)
    {
        int i = 0;
        while (i < file.Length && char.IsDigit(file[i])) i++;
        return i > 0 && int.TryParse(file.Substring(0, i), out var n) ? n : null;
    }

    #endregion
}

//------------------------------------------------------------------------------------- //
