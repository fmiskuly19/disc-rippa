using System.Collections.ObjectModel;

namespace DiscRippa.Core.Compression;

/// <summary>
/// The list of files waiting for HandBrake. Files are compressed one at a time, in order,
/// and the list is saved so it survives closing the app. Everything here is meant to be
/// called from the UI thread.
/// </summary>
public sealed class CompressQueue : ObservableObject
{
    #region Fields

    private readonly IProcessRunner _runner;
    private readonly string? _file;
    private CancellationTokenSource? _cts;
    private CompressJob? _current;
    private int _nextId = 1;
    private bool _paused;
    private double _currentPercent;
    private string _currentText = "Nothing is compressing right now.";

    #endregion

    #region Properties

    public ObservableCollection<CompressJob> Jobs { get; } = new();

    /// <summary>Where HandBrakeCLI is. Null means compression is switched off.</summary>
    public Func<string?> HandBrakePath { get; set; } = () => null;

    public PresetCatalog Catalog { get; set; } = PresetCatalog.Empty;
    public CompressJob? Current => _current;
    public bool Paused => _paused;

    public double CurrentPercent
    {
        get => _currentPercent;
        private set => SetProperty(ref _currentPercent, value);
    }

    public string CurrentText
    {
        get => _currentText;
        private set => SetProperty(ref _currentText, value);
    }

    public int PendingCount => Jobs.Count(j => j.Status is JobStatus.Queued or JobStatus.Compressing);
    public int FailedCount => Jobs.Count(j => j.Status == JobStatus.Failed);

    /// <summary>Looks at the file's resolution (HandBrake's scan) and picks the matching preset.</summary>
    /// <summary>Graphics-card encoders HandBrake can use on this PC ("nvenc", "qsv", "vcn", "mf"); empty = none / not checked.</summary>
    public IReadOnlyCollection<string> HardwareEncoders { get; set; } = Array.Empty<string>();

    /// <summary>Auto uses the graphics card when it can (much faster, light on the CPU).</summary>
    public Func<bool> PreferGraphicsCard { get; set; } = () => false;

    #endregion

    #region Constructors

    /// <summary>Saves the list to <paramref name="storeFile"/>, or keeps it only in memory when null (debug mode).</summary>
    public CompressQueue(IProcessRunner runner, string? storeFile)
    {
        _runner = runner;
        _file = storeFile;
    }

    #endregion

    #region Events

    /// <summary>Raised when a job ends (done, failed or cancelled).</summary>
    public event Action<CompressJob>? JobFinished;

    /// <summary>Raised whenever the list or its counts change.</summary>
    public event Action? Changed;

    #endregion

    #region Methods

    /// <summary>
    /// Adds files. The compressed copy goes to <paramref name="outDir"/>, or next to each original. With
    /// <paramref name="deleteOriginal"/> the original is deleted once its compressed copy has finished successfully.
    /// </summary>
    public int Add(IEnumerable<string> paths, string? outDir, string preset, bool deleteOriginal = false, bool saveAsMkv = false)
    {
        var waiting = new HashSet<string>(
            Jobs.Where(j => j.Status is JobStatus.Queued or JobStatus.Compressing).Select(j => j.Path),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var p in paths)
        {
            if (!waiting.Add(p)) continue;
            Jobs.Add(new CompressJob
            {
                Id = _nextId++,
                Path = p,
                OutDir = string.IsNullOrWhiteSpace(outDir) ? (Path.GetDirectoryName(p) ?? "") : outDir,
                Preset = preset,
                DeleteOriginal = deleteOriginal,
                SaveAsMkv = saveAsMkv,
                Format = FormatFor(preset, saveAsMkv),
            });
            added++;
        }
        OnChanged();
        Pump();
        return added;
    }

    /// <summary>Takes a job off the list. False if it is compressing right now.</summary>
    public bool Remove(CompressJob job)
    {
        if (job.Status == JobStatus.Compressing) return false;
        Jobs.Remove(job);
        OnChanged();
        return true;
    }

    /// <summary>Removes every finished, failed and cancelled job from the list.</summary>
    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled).ToList())
            Jobs.Remove(job);
        OnChanged();
    }

    /// <summary>Puts a failed or cancelled job back in the queue.</summary>
    public void Retry(CompressJob job)
    {
        if (job.Status is not (JobStatus.Failed or JobStatus.Cancelled)) return;
        job.Percent = 0;
        job.Eta = "";
        job.Error = "";
        job.Output = "";
        job.Status = JobStatus.Queued;
        OnChanged();
        Pump();
    }

    /// <summary>Cancels a waiting job, or stops it if it is the one running.</summary>
    public void Cancel(CompressJob job)
    {
        if (job.Status == JobStatus.Queued)
        {
            job.Status = JobStatus.Cancelled;
            OnChanged();
        }
        else if (job.Status == JobStatus.Compressing)
        {
            StopCurrent();
        }
    }

    /// <summary>Stops the running job. The unfinished output is removed; the original is never touched.</summary>
    public void StopCurrent() => _cts?.Cancel();

    /// <summary>Pauses or resumes the queue. The job compressing now carries on either way.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        OnPropertyChanged(nameof(Paused));
        UpdateCurrentText();
        if (!paused) Pump();
    }

    /// <summary>Starts the next waiting job if nothing is running and the queue isn't paused.</summary>
    public void Pump()
    {
        if (_current != null || _paused || HandBrakePath() == null) return;
        var next = Jobs.FirstOrDefault(j => j.Status == JobStatus.Queued);
        if (next == null) return;
        _ = RunJobAsync(next);
    }

    /// <summary>Compresses one job, then starts the next.</summary>
    private async Task RunJobAsync(CompressJob job)
    {
        _cts = new CancellationTokenSource();
        _current = job;
        job.Percent = 0;
        job.Eta = "";
        job.Error = "";
        job.Resolved = "";
        job.Note = "";
        job.Status = JobStatus.Compressing;
        CurrentPercent = 0;
        OnPropertyChanged(nameof(Current));
        UpdateCurrentText();
        OnChanged();

        try
        {
            await ProcessJobAsync(job, _cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            FileNames.TryDelete(job.Partial);
            job.Error = "Something unexpected went wrong: " + ex.Message;
            job.Status = JobStatus.Failed;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _current = null;
            CurrentPercent = 0;
            OnPropertyChanged(nameof(Current));
            UpdateCurrentText();
        }

        OnChanged();
        try
        {
            JobFinished?.Invoke(job);
        }
        catch
        {
            /* a bad listener must not stop the queue */
        }
        Pump();
    }

    /// <summary>Works out the preset, runs HandBrake and handles the result of one job.</summary>
    private async Task ProcessJobAsync(CompressJob job, CancellationToken ct)
    {
        var handbrake = HandBrakePath();
        if (handbrake == null)
        {
            Fail(job, "HandBrakeCLI wasn't found.");
            return;
        }

        var source = job.Path;
        if (!File.Exists(source))
        {
            Fail(job, "The source file no longer exists.");
            return;
        }

        try
        {
            Directory.CreateDirectory(job.OutDir);
        }
        catch (Exception ex)
        {
            Fail(job, $"Can't use the output folder: {ex.Message}");
            return;
        }

        var preset = job.Preset;
        string? softwareFallback = null;   //set when Auto picked a graphics-card preset
        if (preset == Presets.Auto)
        {
            var pick = await ResolveAutoPresetAsync(handbrake, source, ct).ConfigureAwait(true);
            preset = pick.Preset;
            softwareFallback = pick.SoftwareFallback;
            job.Resolved = preset;
            job.Note = pick.Note;
            UpdateCurrentText();
        }

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                job.Status = JobStatus.Cancelled;
                return;
            }
            //MKV: keep every track exactly. MP4 on the graphics card: add the surround track its presets leave out.
            var extra = job.SaveAsMkv ? HandBrake.KeepAllTracksArgs
                      : softwareFallback != null ? HandBrake.SurroundAudioArgs
                      : null;
            var result = await EncodeAsync(job, handbrake, source, preset, ct, extra).ConfigureAwait(true);
            if (result != EncodeResult.Failed || softwareFallback == null) return;

            //the graphics card couldn't do it (driver, too old a card, out of video memory…): do it on the CPU instead.
            job.Note = (job.Note.Length > 0 ? job.Note + "\n" : "") +
                       $"The graphics card encode ({preset}) failed, so it was done with {softwareFallback} on the CPU instead.";
            preset = softwareFallback;
            softwareFallback = null;
            job.Resolved = preset;
            job.Error = "";
            job.Status = JobStatus.Compressing;
            job.Percent = 0;
            UpdateCurrentText();
        }
    }

    /// <summary>What the queue shows in its Format column. Auto's presets are all MP4; a chosen preset brings its own type.</summary>
    private string FormatFor(string preset, bool saveAsMkv)
        => saveAsMkv ? "MKV (all tracks)"
         : preset == Presets.Auto ? "MP4"
         : Catalog.ExtensionFor(preset).TrimStart('.').ToUpperInvariant();

    /// <summary>Runs HandBrakeCLI for one file and reports its progress on the job.</summary>
    private async Task<EncodeResult> EncodeAsync(CompressJob job, string handbrake, string source, string preset, CancellationToken ct,
                                                 IReadOnlyList<string>? extraArgs = null)
    {
        //the preset decides the container (unless MKV was asked for), so the file extension has to match it.
        var extension = extraArgs != null && extraArgs.Contains("av_mkv") ? ".mkv" : Catalog.ExtensionFor(preset);
        var baseName = Path.GetFileNameWithoutExtension(source);
        var output = Path.Combine(job.OutDir, baseName + extension);
        if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            output = Path.Combine(job.OutDir, $"{baseName} (compressed){extension}"); //never overwrite the original
        output = FileNames.UniquePath(output);
        job.Partial = output;

        var args = new List<string> { "-i", source, "-o", output, "--preset", preset };
        if (extraArgs != null) args.AddRange(extraArgs);   //after --preset, so they override its settings
        var tail = new Queue<string>();   //last few non-progress lines, shown if it fails

        var outcome = await _runner.RunAsync(handbrake, args, line =>
        {
            if (HandBrake.TryParseProgress(line, out var pct, out var eta))
            {
                job.Percent = pct;
                job.Eta = eta;
                CurrentPercent = pct;
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                tail.Enqueue(line.Trim());
                while (tail.Count > 6) tail.Dequeue();
            }
        }, ct).ConfigureAwait(true);

        if (outcome.Cancelled || ct.IsCancellationRequested)
        {
            FileNames.TryDelete(output);   //only the unfinished output; the original is untouched
            job.Status = JobStatus.Cancelled;
            return EncodeResult.Cancelled;
        }

        if (outcome.StartFailed)
        {
            FileNames.TryDelete(output);
            Fail(job, $"Couldn't run HandBrake: {outcome.Error}");
            return EncodeResult.Failed;
        }

        if (outcome.ExitCode != 0 || !File.Exists(output))
        {
            FileNames.TryDelete(output);
            var message = "HandBrake couldn't convert this file (original kept).";
            if (tail.Count > 0) message += "\nLast messages:\n" + string.Join("\n", tail);
            message += "\nCommand: " + HandBrake.FormatCommand(handbrake, args);
            Fail(job, message);
            return EncodeResult.Failed;
        }

        job.Output = output;
        job.Percent = 100;
        if (job.DeleteOriginal) DeleteOriginalIfSafe(job, source, output);
        job.Status = JobStatus.Done;
        return EncodeResult.Done;
    }

    /// <summary>
    /// Deletes the ripped MKV, but only when the compressed copy really is there: HandBrake succeeded, the file exists
    /// and isn't suspiciously small. Otherwise the original stays and the job says why.
    /// </summary>
    private static void DeleteOriginalIfSafe(CompressJob job, string source, string output)
    {
        long src, outLen;
        try
        {
            src = new FileInfo(source).Length;
            outLen = new FileInfo(output).Length;
        }
        catch
        {
            AddNote(job, "The original was kept (couldn't check the compressed copy).");
            return;
        }
        if (outLen <= 0 || (outLen < 1_000_000 && outLen * 200 < src))
        {
            AddNote(job, "The original was kept: the compressed copy looks too small to be complete.");
            return;
        }
        if (FileNames.TryDelete(source))
        {
            job.OriginalDeleted = true;
            AddNote(job, "The original MKV was deleted, as chosen when ripping.");

            //"Movie (compressed).mkv" can take the original's name now that it's gone.
            var plain = Path.Combine(Path.GetDirectoryName(output) ?? "", Path.GetFileName(source));
            if (Path.GetFileNameWithoutExtension(output).EndsWith(" (compressed)", StringComparison.Ordinal)
                && string.Equals(Path.GetExtension(output), Path.GetExtension(source), StringComparison.OrdinalIgnoreCase)
                && !File.Exists(plain))
            {
                try
                {
                    File.Move(output, plain);
                    job.Output = plain;
                }
                catch
                {
                }
            }
        }
        else
        {
            AddNote(job, "Couldn't delete the original MKV (is it open in another program?), so it's still there.");
        }
    }

    /// <summary>Adds a line to the note shown with a job.</summary>
    private static void AddNote(CompressJob job, string text) => job.Note = job.Note.Length > 0 ? job.Note + "\n" + text : text;

    /// <summary>Marks a job as failed with the reason.</summary>
    private static void Fail(CompressJob job, string message)
    {
        job.Error = message;
        job.Status = JobStatus.Failed;
    }

    /// <summary>Picks the preset for "Auto" from the file's resolution, preferring the graphics card when allowed.</summary>
    private async Task<(string Preset, string Note, string? SoftwareFallback)> ResolveAutoPresetAsync(string handbrake, string source, CancellationToken ct)
    {
        var lines = new List<string>();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await _runner.RunAsync(handbrake, new[] { "-i", source, "-t", "0" }, lines.Add, limit.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }

        var size = HandBrake.ParseSourceSize(string.Join("\n", lines));
        int height = size?.Height ?? 1080, width = size?.Width ?? 1920;
        var software = HandBrake.ChooseAutoPreset(height, Catalog.Names, width);
        var note = size == null ? "Couldn't read this file's resolution, so a 1080p preset was used." : $"Source is {width}x{height}.";

        if (PreferGraphicsCard())
        {
            var gpu = HandBrake.ChooseHardwarePreset(height, width, HardwareEncoders, Catalog.Names);
            if (gpu != null)
                return (gpu.Value.Preset, note + $" Using the graphics card ({gpu.Value.Name}), keeping the surround sound.", software);
        }
        return (software, note, null);
    }

    /// <summary>Refreshes the "now compressing" line under the list.</summary>
    private void UpdateCurrentText()
    {
        if (_current != null)
        {
            var text = $"Compressing: {_current.FileName}";
            if (_current.Resolved.Length > 0) text += $"  ({_current.Resolved})";
            if (_paused) text += "  (queue pauses after this one)";
            CurrentText = text;
        }
        else
        {
            CurrentText = _paused ? "Queue paused." : "Nothing is compressing right now.";
        }
    }

    /// <summary>Saves the list and tells the screen it changed.</summary>
    private void OnChanged()
    {
        Save();
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(FailedCount));
        Changed?.Invoke();
    }

    /// <summary>Writes the list to disk (unless it lives in memory only).</summary>
    private void Save()
    {
        if (_file == null) return;
        JsonFile.Save(_file, Jobs.Select(j => new JobDto
        {
            Id = j.Id, Path = j.Path, OutDir = j.OutDir, Preset = j.Preset,
            Status = j.Status.ToString(), Output = j.Output, Error = j.Error,
            DeleteOriginal = j.DeleteOriginal, OriginalDeleted = j.OriginalDeleted, SaveAsMkv = j.SaveAsMkv,
        }).ToList());
    }

    /// <summary>Loads the saved list. A job that was mid-compression goes back to Queued and starts over.</summary>
    public void Load()
    {
        if (_file == null) return;
        var saved = JsonFile.Load<List<JobDto>>(_file);
        if (saved == null) return;
        foreach (var d in saved)
        {
            if (!Enum.TryParse<JobStatus>(d.Status, out var status)) status = JobStatus.Queued;
            if (status == JobStatus.Compressing) status = JobStatus.Queued;
            var job = new CompressJob
            {
                Id = d.Id, Path = d.Path, OutDir = d.OutDir, Preset = d.Preset,
                Output = d.Output, Error = d.Error, Status = status,
                DeleteOriginal = d.DeleteOriginal, OriginalDeleted = d.OriginalDeleted, SaveAsMkv = d.SaveAsMkv,
                Format = FormatFor(d.Preset, d.SaveAsMkv),
                Percent = status == JobStatus.Done ? 100 : 0,
            };
            Jobs.Add(job);
            _nextId = Math.Max(_nextId, d.Id + 1);
        }
        OnChanged();
    }

    /// <summary>Stops the running job right now (used when the app closes) and puts it back in the queue.</summary>
    public void Shutdown()
    {
        var job = _current;
        _cts?.Cancel();
        if (job != null)
        {
            Thread.Sleep(300);               //let HandBrake release the half-written file
            FileNames.TryDelete(job.Partial);
            job.Status = JobStatus.Queued;   //it restarts next time the app opens
        }
        Save();
    }

    #endregion

    #region Nested types

    private enum EncodeResult { Done, Failed, Cancelled }

    internal sealed class JobDto
    {
        #region Properties

        public int Id { get; set; }
        public string Path { get; set; } = "";
        public string OutDir { get; set; } = "";
        public string Preset { get; set; } = Presets.Auto;
        public string Status { get; set; } = nameof(JobStatus.Queued);
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public bool DeleteOriginal { get; set; }
        public bool OriginalDeleted { get; set; }
        public bool SaveAsMkv { get; set; }

        #endregion
    }

    #endregion
}
