namespace DiscRippa.Core.Video;

/// <summary>Waits for a disc to appear in a drive, then reads its list of titles.</summary>
public sealed class DiscWatcher
{
    #region Fields

    private readonly IProcessRunner _runner;
    private readonly Func<string?> _makeMkvPath;
    private readonly TimeSpan _pollInterval;

    #endregion

    #region Properties

    /// <summary>
    /// The disc we just finished with. While it is still in the drive it is ignored, so "wait for the
    /// next disc" can never rip the same disc twice. Cleared as soon as the drive is empty.
    /// </summary>
    public DiscKey? Ignore { get; set; }

    /// <summary>How long to wait before trying a read again when the drive isn't ready yet.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    #endregion

    #region Constructors

    /// <summary>Creates a watcher that asks MakeMKV about the drives every <paramref name="pollInterval"/>.</summary>
    public DiscWatcher(IProcessRunner runner, Func<string?> makeMkvPath, TimeSpan pollInterval)
    {
        _runner = runner;
        _makeMkvPath = makeMkvPath;
        _pollInterval = pollInterval;
    }

    #endregion

    #region Events

    /// <summary>Raised after every look at the drives, with what each one is doing.</summary>
    public event Action<IReadOnlyList<DriveStatus>>? DrivesChanged;

    #endregion

    #region Methods

    /// <summary>A one-off look at the drives (used when the app opens).</summary>
    public async Task<IReadOnlyList<DriveStatus>> CheckDrivesAsync(CancellationToken ct)
    {
        var lines = await ListDrivesAsync(ct).ConfigureAwait(true);
        var drives = MakeMkvParser.ParseDrives(lines);
        DrivesChanged?.Invoke(drives);
        return drives;
    }

    /// <summary>
    /// Waits for a (new) disc and reads its titles. While reading, <paramref name="onReadMessage"/> gets
    /// MakeMKV's messages and <paramref name="onReadProgress"/> its progress (0-100) when it reports any.
    /// Throws OperationCanceledException if cancelled.
    /// </summary>
    public async Task<DiscFound> WaitForDiscAsync(Action<string>? onDiscFound, CancellationToken ct,
                                                  Action<string>? onReadMessage = null, Action<double>? onReadProgress = null)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var drive = await FindDiscAsync(ct).ConfigureAwait(true);

            if (drive != null && Ignore is DiscKey ignored
                && ignored.Label == drive.DiscName && ignored.Device == drive.Device)
            {
                drive = null;          //still the disc we just finished; wait for it to be taken out
            }
            else if (drive == null)
            {
                Ignore = null;         //empty drive: whatever goes in next is a new disc
            }

            if (drive != null)
            {
                Ignore = null;
                onDiscFound?.Invoke(drive.DiscName);
                var messages = new List<string>();
                const int attempts = 3;
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    var (titles, info) = await ScanTitlesAsync(drive.Index, ct, m =>
                    {
                        messages.Add(m);
                        onReadMessage?.Invoke(m);
                    }, onReadProgress).ConfigureAwait(true);

                    if (titles.Count > 0)
                        return new DiscFound(drive.Index, drive.DiscName, drive.Device, titles, info.Type, info.Name, drive.DriveName);

                    if (attempt < attempts)
                    {
                        //often the drive is still spinning up (e.g. right after an aborted rip). Give it a moment.
                        onReadMessage?.Invoke($"Nothing read yet — the drive may still be getting ready. Trying again ({attempt + 1} of {attempts})…");
                        await Task.Delay(RetryDelay, ct).ConfigureAwait(true);
                    }
                }

                var reason = messages.Any(m => m.Contains("can't find any usable optical drive", StringComparison.OrdinalIgnoreCase)
                                           || m.Contains("cannot find any usable optical drive", StringComparison.OrdinalIgnoreCase))
                    //the drive itself vanished while the disc was being read (MakeMKV and Windows both lost it).
                    ? "The disc drive disappeared while the disc was being read, so Windows and MakeMKV can no longer see it.\n\n" +
                      "This usually means a USB drive lost power or its connection: Blu-rays need more power to spin up than DVDs or CDs. " +
                      "Plug the drive straight into a USB port on the back of the computer (both plugs, if its cable has two), " +
                      "or unplug it and plug it back in, then try again."
                    : messages.Any(m => m.Contains("shareware", StringComparison.OrdinalIgnoreCase)
                                        || m.Contains("evaluation period", StringComparison.OrdinalIgnoreCase)
                                        || m.Contains("registration key", StringComparison.OrdinalIgnoreCase)
                                        || m.Contains("evaluation period has expired", StringComparison.OrdinalIgnoreCase))
                    //blu-ray support in MakeMKV needs its evaluation started (or a key), which only its own window can do.
                    ? "MakeMKV needs a key before it will read Blu-rays (DVDs don't need one).\n\n" +
                      "Open Settings and click \"Activate Blu-ray\" to fetch MakeMKV's free beta key and install it. " +
                      "Then try this disc again."
                    : "No titles could be read from this disc. It may be dirty, damaged or unsupported, or the drive wasn't ready.";
                var last = messages.Where(m => !m.StartsWith("Nothing read yet", StringComparison.Ordinal)).TakeLast(4).ToList();
                if (last.Count > 0) reason += "\n\nWhat MakeMKV said:\n- " + string.Join("\n- ", last);
                throw new DiscReadException(reason, drive.DiscName, drive.Device);
            }

            await Task.Delay(_pollInterval, ct).ConfigureAwait(true);
        }
    }

    /// <summary>The drive with a usable disc in it (not the ignored one), or null.</summary>
    private async Task<DiscDrive?> FindDiscAsync(CancellationToken ct)
    {
        var lines = await ListDrivesAsync(ct).ConfigureAwait(true);
        if (lines.Count == 0) return null;   //no answer this time (busy or timed out); look again shortly
        DrivesChanged?.Invoke(MakeMkvParser.ParseDrives(lines));
        return MakeMkvParser.FindDisc(lines);
    }

    /// <summary>MakeMKV's drive list, as raw lines.</summary>
    private async Task<List<string>> ListDrivesAsync(CancellationToken ct)
    {
        var exe = _makeMkvPath() ?? throw new InvalidOperationException("MakeMKV wasn't found.");
        var lines = new List<string>();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(90));
        await _runner.RunAsync(exe, new[] { "-r", "--cache=1", "info", "disc:9999" }, lines.Add, limit.Token)
            .ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        return lines;
    }

    /// <summary>Reads the disc's titles with MakeMKV.</summary>
    private async Task<(IReadOnlyList<TitleInfo> Titles, DiscInfo Info)> ScanTitlesAsync(
        int driveIndex, CancellationToken ct, Action<string>? onMessage, Action<double>? onProgress)
    {
        var exe = _makeMkvPath() ?? throw new InvalidOperationException("MakeMKV wasn't found.");
        var lines = new List<string>();
        await _runner.RunAsync(exe, new[] { "-r", "--progress=-same", "info", $"disc:{driveIndex}" }, line =>
        {
            lines.Add(line);
            var message = MakeMkvParser.ParseMessage(line);
            if (message != null)
            {
                onMessage?.Invoke(message);
                return;
            }
            var progress = MakeMkvParser.ParseProgressValues(line);
            if (progress != null) onProgress?.Invoke(progress.Value.Total);
        }, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        return (MakeMkvParser.ParseTitles(lines), MakeMkvParser.ParseDiscInfo(lines));
    }

    #endregion
}
