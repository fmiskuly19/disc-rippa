namespace DiscRippa.Core.Video;

/// <summary>Rips the chosen titles from a disc into MKV files, one title at a time.</summary>
public sealed class RipSession
{
    #region Fields

    private readonly IProcessRunner _runner;
    private readonly Func<string?> _makeMkvPath;

    //MKV files already in the folder before the rip, with when they were last written. A file counts as this
    //rip's if it is new OR has been written since (a leftover "title_t00.mkv" from a crash gets overwritten).
    private Dictionary<string, DateTime> _before = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _done = new();
    private string _destDir = "";

    #endregion

    #region Constructors

    /// <summary>Creates a session that runs the MakeMKV at <paramref name="makeMkvPath"/>.</summary>
    public RipSession(IProcessRunner runner, Func<string?> makeMkvPath)
    {
        _runner = runner;
        _makeMkvPath = makeMkvPath;
    }

    #endregion

    #region Methods

    /// <summary>Rips the chosen titles one by one and names the files.</summary>
    public async Task<RipResult> RunAsync(RipRequest request, Action<RipProgress>? onProgress,
                                          Action<int>? onProblemCount, CancellationToken ct,
                                          Action<string>? onMessage = null)
    {
        var result = new RipResult();
        var exe = _makeMkvPath();
        if (exe == null)
        {
            result.Error = "MakeMKV wasn't found.";
            return result;
        }

        _destDir = request.DestDir;
        _done.Clear();
        _before = SnapshotMkvFiles(_destDir);
        int count = request.Titles.Count;
        int failedTitles = 0;

        for (int n = 1; n <= count; n++)
        {
            var title = request.Titles[n - 1];
            int number = n;
            double overall = (number - 1) / (double)count * 100.0;

            //MakeMKV works in stages ("PRGT"), each with steps ("PRGC") and its own 0-100%. Only the
            //saving stage drives the progress bar; otherwise the bar would run to 100, drop to 0, and climb again.
            string stage = "", step = "";
            bool sawStage = false, saving = false;
            double stagePct = 0, stepPct = 0;

            void Report() => onProgress?.Invoke(new RipProgress(
                number, count, title.Duration, overall, stage, step, stagePct, stepPct, saving || !sawStage));

            Report();

            var args = new[] { "-r", "--progress=-same", "mkv", $"disc:{request.DriveIndex}", title.Id.ToString(), request.DestDir };
            var outcome = await _runner.RunAsync(exe, args, line =>
            {
                var progressTitle = MakeMkvParser.ParseProgressTitle(line);
                if (progressTitle != null)
                {
                    if (progressTitle.IsTotal)
                    {
                        stage = progressTitle.Name;
                        sawStage = true;
                        stagePct = 0;
                        //saving has started if the stage says so, or (for other languages) the MKV file has appeared.
                        saving = MakeMkvParser.IsSavingStage(stage) || NewMkvFileExists(request.DestDir);
                        onMessage?.Invoke($"— {stage} —");
                    }
                    else
                    {
                        step = progressTitle.Name;
                        stepPct = 0;
                        if (sawStage && !saving && NewMkvFileExists(request.DestDir)) saving = true;
                    }
                    Report();
                    return;
                }

                var values = MakeMkvParser.ParseProgressValues(line);
                if (values != null)
                {
                    stepPct = values.Value.Current;
                    stagePct = values.Value.Total;
                    if (saving || !sawStage)
                        overall = Math.Max(overall, ((number - 1) + stagePct / 100.0) / count * 100.0);
                    Report();
                    return;
                }

                var message = MakeMkvParser.ParseMessage(line);
                if (message == null) return;

                result.Log.Add(message);
                onMessage?.Invoke(message);
                var copy = MakeMkvParser.ParseCopyComplete(message);
                if (copy != null)
                    failedTitles += copy.Value.Failed;
                else if (MakeMkvParser.LooksLikeError(message))
                {
                    result.Problems.Add(message);
                    onProblemCount?.Invoke(result.Problems.Count);
                }
            }, ct).ConfigureAwait(true);

            if (outcome.Cancelled || ct.IsCancellationRequested)
            {
                result.RemovedPartialFiles = RemovePartialFiles();
                result.Cancelled = true;
                result.Files.AddRange(_done);
                return result;
            }

            if (outcome.StartFailed || outcome.ExitCode != 0 || failedTitles > 0)
            {
                result.RemovedPartialFiles = RemovePartialFiles();
                result.Error = BuildError(result, outcome);
                result.Files.AddRange(_done);
                return result;
            }

            if (RenameNewFiles(request, n, count, title) == 0)
            {
                result.Error = $"MakeMKV said title {number} finished, but no new MKV file appeared in:\n{request.DestDir}";
                if (_done.Count > 0) result.Error += "\n\n" + EarlierKept(_done.Count);
                result.Files.AddRange(_done);
                return result;
            }
        }

        result.Files.AddRange(_done);
        return result;
    }

    /// <summary>
    /// Deletes MKV files created by this rip that are not finished (used on abort, failure, and when the
    /// app is closed mid-rip). Files from titles that already completed are kept.
    /// </summary>
    public int RemovePartialFiles()
    {
        if (_destDir.Length == 0 || !Directory.Exists(_destDir)) return 0;
        int removed = 0;
        foreach (var name in ListMkvFiles(_destDir))
        {
            if (!IsFromThisRip(name) || _done.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (FileNames.TryDelete(Path.Combine(_destDir, name))) removed++;
        }
        return removed;
    }

    /// <summary>Gives this title's new file(s) their proper names. Returns how many there were.</summary>
    private int RenameNewFiles(RipRequest request, int number, int count, TitleInfo title)
    {
        var fresh = ListMkvFiles(request.DestDir)
            .Where(f => IsFromThisRip(f) && !_done.Contains(f, StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in fresh)
        {
            var target = FileNames.UniquePath(Path.Combine(request.DestDir, TitleFileName(request.BaseName, number, count, title)));
            File.Move(Path.Combine(request.DestDir, file), target);
            _done.Add(Path.GetFileName(target));
        }
        return fresh.Count;
    }

    /// <summary>
    /// The file a title is saved as: "Name.mkv" for a single title; with several, numbered and with the title's source
    /// on the disc, so they can be told apart: "Name - 01 (00800.mpls).mkv".
    /// </summary>
    public static string TitleFileName(string baseName, int number, int count, TitleInfo title)
    {
        if (count == 1) return baseName + ".mkv";
        var source = title.SourceFile.Trim().Length > 0 ? $" ({FileNames.Safe(title.SourceFile)})" : "";
        return $"{baseName} - {number:00}{source}.mkv";
    }

    /// <summary>Bytes written so far by this rip (finished titles plus the one being saved).</summary>
    public long BytesWritten()
    {
        if (_destDir.Length == 0) return 0;
        long total = 0;
        try
        {
            foreach (var path in Directory.GetFiles(_destDir, "*.mkv"))
            {
                if (!IsFromThisRip(Path.GetFileName(path))) continue;
                try
                {
                    total += new FileInfo(path).Length;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return total;
    }

    /// <summary>True if this rip has written an MKV file in <paramref name="dir"/>.</summary>
    private bool NewMkvFileExists(string dir)
        => ListMkvFiles(dir).Any(f => IsFromThisRip(f) && !_done.Contains(f, StringComparer.OrdinalIgnoreCase));

    /// <summary>True if the file is new or changed since the rip started.</summary>
    private bool IsFromThisRip(string name)
    {
        if (!_before.TryGetValue(name, out var writtenBefore)) return true;
        try
        {
            return File.GetLastWriteTimeUtc(Path.Combine(_destDir, name)) != writtenBefore;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The MKV files in a folder and when each was last written.</summary>
    private static Dictionary<string, DateTime> SnapshotMkvFiles(string dir)
    {
        var snapshot = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ListMkvFiles(dir))
        {
            try
            {
                snapshot[name] = File.GetLastWriteTimeUtc(Path.Combine(dir, name));
            }
            catch
            {
            }
        }
        return snapshot;
    }

    /// <summary>The names of the MKV files in a folder.</summary>
    private static HashSet<string> ListMkvFiles(string dir)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.mkv")) set.Add(Path.GetFileName(f));
        }
        catch
        {
        }
        return set;
    }

    /// <summary>A readable explanation of why the rip failed.</summary>
    private string BuildError(RipResult result, ProcessOutcome outcome)
    {
        var text = new System.Text.StringBuilder();
        if (outcome.StartFailed)
        {
            text.Append("MakeMKV could not be started: ").Append(outcome.Error);
            return text.ToString();
        }

        text.Append("MakeMKV could not finish ripping this disc.");

        var reasons = result.Problems.Count > 0
            ? result.Problems.Take(5).ToList()
            : result.Log.Skip(Math.Max(0, result.Log.Count - 3)).ToList();
        if (reasons.Count > 0)
            text.Append("\n\nWhat it reported:\n- ").Append(string.Join("\n- ", reasons));

        if (result.RemovedPartialFiles > 0)
            text.Append("\n\nThe unfinished file was removed.");
        if (_done.Count > 0)
            text.Append("\n\n" + EarlierKept(_done.Count));
        return text.ToString();
    }

    /// <summary>"The title ripped before this one was kept." (or "The 3 titles ...were kept.")</summary>
    private static string EarlierKept(int count) => count == 1
        ? "The title ripped before this one was kept."
        : $"The {count} titles ripped before this one were kept.";

    #endregion
}
