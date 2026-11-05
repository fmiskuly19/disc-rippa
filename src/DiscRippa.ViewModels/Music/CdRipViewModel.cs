namespace DiscRippa.ViewModels.Music;

/// <summary>
/// The Music CD tab: wait for a music CD, read its names (from the disc, or online if it has none), show the CD screen,
/// rip the chosen tracks to FLAC with cyanrip, then (optionally) wait for the next CD. Works like the DVD / Blu-ray tab.
/// </summary>
public sealed partial class CdRipViewModel : ObservableObject
{
    #region Fields

    private readonly CdWatcher _watcher;
    private readonly ToolRunners _runners;
    private readonly IToolPaths _tools;
    private readonly ISettingsService _settings;
    private readonly RunMode _mode;
    private readonly AppFolders _folders;
    private readonly HistoryStore _history;
    private readonly IDriveService _drives;
    private readonly ICdDriveProbe _probe;
    private readonly Simulators _simulators;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notify;
    private readonly ToolPicker _picker;
    private CancellationTokenSource? _cts;
    private Ticker? _idleTicker;
    private bool _stopping;
    private int _ripNumber;
    private string _lastRipName = "The CD";
    private string _lastRipFolder = "";
    private string _carryWarning = "";
    private string? _automationProblem;
    private CdDriveState? _lastDrive;   //what was in the drive last time it was checked
    private IReadOnlyDictionary<int, string> _trackTitles = new Dictionary<int, string>();
    private bool _checking;
    private bool _syncingSettings;
    private string? _driveSignature;

    #endregion

    #region Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsReviewing), nameof(ShowStatusCard), nameof(CanChangeAutomation), nameof(PrimaryEnabled))]
    private RipState state = RipState.Idle;

    [ObservableProperty] private string status = "Insert a music CD and click Start.";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool indeterminate;
    [ObservableProperty] private string buttonText = "Start";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string warning = "";

    public bool HasWarning => Warning.Length > 0;

    /// <summary>The CD screen while it's showing; null otherwise.</summary>
    [ObservableProperty] private CdReviewViewModel? review;

    [ObservableProperty] private string driveTitle = "Looking for your CD drive…";
    [ObservableProperty] private string driveState = "";
    public CdRipStats Stats { get; } = new();

    /// <summary>Everything cyanrip says during the rip.</summary>
    public LogViewModel Log { get; } = new();

    public bool IsBusy => State != RipState.Idle;
    public bool IsReviewing => State == RipState.Reviewing;
    public bool ShowStatusCard => State != RipState.Reviewing;

    /// <summary>The "cyanrip not found" banner.</summary>
    public bool CyanripMissing => _tools.Cyanrip == null;

    /// <summary>Start needs cyanrip, and is off while a DVD / Blu-ray is in. Cancel / Abort always works.</summary>
    public bool PrimaryEnabled => _tools.Cyanrip != null && !(HasOtherDisc && State == RipState.Idle);

    /// <summary>True while the Music CD side is showing. The drive is only watched then.</summary>
    public bool IsActive { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoNextEnabled))]
    private bool automation;

    [ObservableProperty] private bool autoNext;
    public bool AutoNextEnabled => !Automation && !HasOtherDisc;

    /// <summary>
    /// Automation mode can't be switched while a CD is being read, shown or ripped, only while idle or waiting
    /// for one (so a job never changes rules half-way through).
    /// </summary>
    public bool CanChangeAutomation => (State is RipState.Idle or RipState.Waiting) && !HasOtherDisc;

    /// <summary>
    /// A disc that isn't a music CD is in the drive (a DVD, Blu-ray or data disc). Shown as a warning with a button to
    /// switch to DVD / Blu-ray mode; Start and the checkboxes are off meanwhile.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoNextEnabled), nameof(CanChangeAutomation), nameof(PrimaryEnabled))]
    private bool hasOtherDisc;

    #endregion

    #region Constructors

    /// <summary>Creates the Music CD tab.</summary>
    public CdRipViewModel(CdWatcher watcher, ToolRunners runners, IToolPaths tools, ISettingsService settings, RunMode mode,
                          AppFolders folders, HistoryStores history, IDriveService drives, ICdDriveProbe probe, Simulators simulators,
                          IDialogService dialogs, INotificationService notify, ToolPicker picker)
    {
        _watcher = watcher;
        _runners = runners;
        _tools = tools;
        _settings = settings;
        _mode = mode;
        _folders = folders;
        _history = history.Music;
        _drives = drives;
        _probe = probe;
        _simulators = simulators;
        _dialogs = dialogs;
        _notify = notify;
        _picker = picker;

        automation = settings.Current.CdAutomation;
        autoNext = settings.Current.CdAutoNext;

        _watcher.DrivesChanged += OnDrivesChanged;
        _drives.DriveNamesChanged += () =>
        {
            if (IsActive && State == RipState.Idle) _ = CheckDrivesAsync();
        };
        _tools.Changed += () =>
        {
            OnPropertyChanged(nameof(CyanripMissing));
            OnPropertyChanged(nameof(PrimaryEnabled));
        };
        _settings.Saved += SyncFromSettings;
    }

    #endregion

    #region Commands

    /// <summary>The big button: Start / Cancel / Abort rip. Concurrent so it stays clickable while its own rip runs.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PrimaryAsync()
    {
        switch (State)
        {
            case RipState.Idle:
                await StartAsync(fresh: true);
                break;
            case RipState.Ripping:
                ConfirmAbort();
                break;
            case RipState.Reviewing:
                Review?.Cancel();
                break;
            default:
                _cts?.Cancel();
                break;
        }
    }

    /// <summary>Lets the user pick cyanrip, then looks at the drive again.</summary>
    [RelayCommand]
    private async Task LocateCyanripAsync()
    {
        if (_picker.LocateCyanrip()) await SetActiveAsync(IsActive);
    }

    #endregion

    #region Methods

    /// <summary>Called when auto next changes.</summary>
    partial void OnAutoNextChanged(bool value)
    {
        if (_syncingSettings) return;
        _settings.Current.CdAutoNext = value;
        _settings.Save();
    }

    /// <summary>Called when automation changes.</summary>
    partial void OnAutomationChanged(bool value)
    {
        if (_syncingSettings) return;
        _settings.Current.CdAutomation = value;
        _settings.Save();
        if (!value) Review?.TakeOver();
        if (State == RipState.Idle)
        {
            _driveSignature = null;
            _ = CheckDrivesAsync();
        }
    }

    /// <summary>Follows changes made in the Settings window without saving them again.</summary>
    private void SyncFromSettings()
    {
        _syncingSettings = true;
        try
        {
            AutoNext = _settings.Current.CdAutoNext;
            Automation = _settings.Current.CdAutomation;
        }
        finally
        {
            _syncingSettings = false;
        }
    }

    /// <summary>Called when has other disc changes.</summary>
    partial void OnHasOtherDiscChanged(bool value)
    {
        if (State != RipState.Waiting) return;
        if (value)
        {
            Status = "That's not a music CD.";
            Detail = "Music CD mode can't rip it. Insert a music CD instead, or switch to DVD / Blu-ray.";
        }
        else
        {
            Status = "Waiting for a music CD… insert one now.";
            Detail = Automation ? "Automation mode: it will be ripped and ejected automatically." : "";
        }
    }

    /// <summary>Stops waiting for a CD (so the app can switch to DVD / Blu-ray mode). Returns once it's idle.</summary>
    public async Task StopWaitingAsync()
    {
        if (State != RipState.Waiting) return;
        _cts?.Cancel();
        for (int i = 0; i < 50 && State != RipState.Idle; i++) await Task.Delay(50);
    }

    /// <summary>Called when this side comes to the front (true) or the DVD / Blu-ray side does (false).</summary>
    public async Task SetActiveAsync(bool active)
    {
        IsActive = active;
        if (!active) return;
        _idleTicker ??= Ticker.Start(_mode.CdPollInterval, async () =>
        {
            if (IsActive && State == RipState.Idle) await CheckDrivesAsync();
        });
        _driveSignature = null;
        await CheckDrivesAsync();
    }

    /// <summary>Asks the CD watcher to look at the drives now.</summary>
    private async Task CheckDrivesAsync()
    {
        if (State != RipState.Idle || _checking) return;
        _checking = true;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _watcher.CheckDrivesAsync(limit.Token);
        }
        catch
        {
            DriveTitle = "Couldn't check the CD drive";
            DriveState = "It will be checked again in a moment.";
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>Updates the drive text and the idle message when what is in the drive changes.</summary>
    private void OnDrivesChanged(IReadOnlyList<CdDriveState> drives)
    {
        if (State.IsWorking()) return;

        var drive = drives.FirstOrDefault(d => d.IsReady && d.IsAudioCd) ?? drives.FirstOrDefault(d => d.IsReady) ?? drives.FirstOrDefault();
        HasOtherDisc = drive is { IsReady: true, IsAudioCd: false };
        _lastDrive = drive;
        if (drive == null)
        {
            DriveTitle = "No CD drive found";
            DriveState = "Connect a CD, DVD or Blu-ray drive.";
        }
        else
        {
            var more = drives.Count > 1 ? $"   (+{Text.Plural(drives.Count - 1, "more drive")})" : "";
            DriveTitle = _drives.DriveName(drive.Letter, $"CD drive ({drive.Letter})") + more;
            DriveState = drive.IsReady && !drive.IsAudioCd
                ? (drive.Label.Length > 0 ? $"Disc inserted: {FileNames.CleanDiscName(drive.Label)} (not a music CD)" : "Disc inserted (not a music CD)")
                : drive.StateText;
        }

        var signature = drive?.Signature ?? "none";
        bool changed = signature != _driveSignature;
        _driveSignature = signature;
        if (State != RipState.Idle || !changed || !IsActive) return;

        bool isNew = drive is { IsAudioCd: true } && drive.Signature != _watcher.Ignore;
        if (Automation && isNew && _tools.Cyanrip != null) _ = StartAsync(fresh: false);
        else ShowIdlePrompt(drive);
    }

    /// <summary>The message to show while nothing is happening, depending on what is in the drive.</summary>
    private void ShowIdlePrompt(CdDriveState? drive)
    {
        ButtonText = "Start";
        if (drive == null)
        {
            Status = "No CD drive found.";
            Detail = "Connect a CD, DVD or Blu-ray drive. It's picked up automatically.";
        }
        else if (drive.IsReady && drive.IsAudioCd)
        {
            Status = $"A music CD is in the drive ({Text.Plural(drive.Tracks.Count, "track")}).";
            Detail = "Click below to read it and choose what to rip.";
            ButtonText = "Read this CD";
        }
        else if (drive.IsReady)
        {
            Status = "That's not a music CD.";
            Detail = "Music CD mode can't rip it. Insert a music CD instead, or switch to DVD / Blu-ray.";
        }
        else if (Automation)
        {
            Status = "Automation mode is on: insert a music CD.";
            Detail = "It will be read, every track ripped to FLAC, and the CD ejected. No clicks needed.";
        }
        else
        {
            Status = "Insert a music CD and click Start.";
            Detail = "You can also click Start first, then insert the CD.";
        }
    }

    /// <summary>Asks before stopping a rip, then stops it.</summary>
    private void ConfirmAbort()
    {
        int rip = _ripNumber;
        if (!_dialogs.Confirm("Stop ripping this CD now?\n\nThe unfinished track is deleted. Tracks that already finished are kept.",
                              "Abort rip?", defaultNo: true))
            return;
        if (State == RipState.Ripping && rip == _ripNumber)
        {
            _stopping = true;
            Status = "Stopping the rip…";
            Detail = "Removing the unfinished track.";
            Log.Add("Abort requested — stopping cyanrip.");
            _cts?.Cancel();
            return;
        }
        _dialogs.Show($"The rip finished while that question was open, so there was nothing left to stop.\n\n" +
                      $"{_lastRipName} was saved in {_lastRipFolder} and is listed on the Ripped CDs tab.\n" +
                      "If you don't want it, use Open folder on that tab and delete the files.",
                      "Already finished");
    }

    /// <summary>Starts waiting for, reading and ripping CDs.</summary>
    /// <param name="fresh">Clicked by a person: whatever is in the drive is fair game, even a CD that was just ripped.</param>
    private async Task StartAsync(bool fresh)
    {
        try
        {
            if (fresh) _watcher.Ignore = null;
            await RunAsync(afterRip: false);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            Fail(ex.Message);
        }
    }

    /// <summary>Stops everything when the app closes.</summary>
    public void Shutdown()
    {
        Review?.Cancel();
        _cts?.Cancel();
        _idleTicker?.Dispose();
        Stats.Stop();
    }

    /// <summary>Waits for a CD, reads it, rips it, and repeats while "Keep Ripping!" is on.</summary>
    private async Task RunAsync(bool afterRip)
    {
        if (_tools.Cyanrip == null)
        {
            Fail("cyanrip wasn't found, so music CDs can't be ripped yet.\n\nIt's included in the tools folder. Or download cyanrip-win64.exe from github.com/cyanreg/cyanrip/releases, then click \"Locate cyanrip…\" on this tab.");
            return;
        }

        while (true)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            State = RipState.Waiting;
            ButtonText = "Cancel";
            Warning = _carryWarning;
            _carryWarning = "";
            Detail = Automation ? "Automation mode: it will be ripped and ejected automatically." : "";
            Progress = 0;
            Indeterminate = true;
            Stats.Hide();
            Status = afterRip ? "Waiting for the next CD… take the finished one out and insert the next."
                              : "Waiting for a music CD… insert one now.";

            //a CD that's already in goes straight to reading: no "insert one now" first.
            if (!afterRip && _lastDrive is { IsReady: true, IsAudioCd: true } inDrive && inDrive.Signature != _watcher.Ignore)
            {
                Status = "Reading the CD…";
                Detail = $"{Text.Plural(inDrive.Tracks.Count, "track")}. Reading the names stored on the disc.";
            }

            CdDriveState cd;
            CdMetadata meta;
            try
            {
                cd = await _watcher.WaitForCdAsync(ct);
                meta = await ReadNamesAsync(cd, ct);
            }
            catch (OperationCanceledException)
            {
                Reset("Cancelled. Click Start when you're ready.");
                return;
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                return;
            }

            if (!await RipCdAsync(cd, meta, ct)) return;
            afterRip = true;
        }
    }

    /// <summary>
    /// The album and track names. The disc's own (CD-Text) come first; only a CD with no track names on it is looked
    /// up online (MusicBrainz, through cyanrip), and only if that's switched on in Settings.
    /// </summary>
    private async Task<CdMetadata> ReadNamesAsync(CdDriveState cd, CancellationToken ct)
    {
        Log.Clear();
        State = RipState.Scanning;
        DriveState = $"Reading: {cd.StateText}";
        Status = "Reading the CD…";
        Detail = "Reading the album and track names stored on the disc (CD-Text).";
        var letter = cd.Letter;
        var model = await Task.Run(() => _probe.DriveModel(letter), ct);

        var meta = await Task.Run(() => _probe.ReadCdText(letter), ct) ?? new CdMetadata();
        if (meta.HasTrackNames)
        {
            Log.Add("Names read from the CD itself (CD-Text); nothing is looked up online.");
        }
        else if (_settings.Current.CdLookup)
        {
            Log.Add("No track names on the CD; looking it up on MusicBrainz.");
            Status = "Looking the CD up online…";
            Detail = "This CD has no names stored on it, so they're being looked up on MusicBrainz. This usually takes a few seconds.";
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(90));
            var online = await new CdRipSession(_runners.Cyanrip, () => _tools.Cyanrip).LookUpAsync(letter, limit.Token);
            ct.ThrowIfCancellationRequested();
            if (online.Found)
            {
                //keep anything the disc did have (e.g. the album name) where MusicBrainz is silent.
                if (online.Album.Length == 0) online.Album = meta.Album;
                if (online.AlbumArtist.Length == 0) online.AlbumArtist = meta.AlbumArtist;
                meta = online;
            }
            else if (online.DriveModel.Length > 0 && model.Length == 0)
            {
                model = online.DriveModel;
            }
        }
        else
        {
            Log.Add("No track names on the CD, and online lookup is off in Settings.");
        }
        if (meta.DriveModel.Length == 0) meta.DriveModel = model;
        return meta;
    }

    /// <summary>Shows the CD screen and rips what was chosen. True if the app should wait for the next CD.</summary>
    private async Task<bool> RipCdAsync(CdDriveState cd, CdMetadata meta, CancellationToken ct)
    {
        bool automated = Automation;
        var (choices, tookOver) = await ShowCdScreenAsync(cd, meta, automated, ct);
        bool quiet = automated && !tookOver;

        if (choices == null)
        {
            if (_automationProblem != null)
            {
                _notify.Notify("Automation stopped", _automationProblem);
                Reset("Automation stopped.");
                Warning = _automationProblem;
            }
            else
            {
                Reset("Cancelled. Click Start when you're ready.");
            }
            return false;
        }

        if (!_mode.IsDebug && !string.Equals(choices.MusicRoot, _folders.MusicFolder, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Current.MusicFolder = choices.MusicRoot;   //remembered for next time
            _settings.Save();
        }

        var folder = UniqueAlbumFolder(choices.Folder);
        _trackTitles = choices.Metadata.TrackTitles;
        var name = choices.Artist.Length > 0 && choices.Album.Length > 0 ? $"{choices.Artist} - {choices.Album}"
                 : choices.Album.Length > 0 ? choices.Album : "Music CD";
        if (meta.TotalDiscs > 1) name += $" (disc {meta.Disc} of {meta.TotalDiscs})";

        var (result, entry) = await RipAsync(cd, meta, choices, folder, name, ct);

        if (result.Cancelled)
        {
            var text = "Rip aborted." + (result.Files.Count > 0 ? $" {Text.Plural(result.Files.Count, "finished track")} kept." : "");
            Log.Add(text);
            _history.EndUnfinishedRip(entry, result.Files, result.Problems);
            Reset(text, keepStats: true);
            return false;
        }

        if (!result.Success)
        {
            Log.Add("The rip failed: " + result.Error);
            _history.EndUnfinishedRip(entry, result.Files, result.Problems);
            if (quiet)
            {
                await SkipCdAsync(cd, $"Ripping {name} failed", result.Error);
                return true;
            }
            Reset("Something went wrong with that CD.", keepStats: true);
            _notify.Notify("Rip failed", Text.FirstLine(result.Error));
            _dialogs.Show($"{result.Error}\n\nFull log: {_mode.LastCdRipLog}", "Rip failed", DialogIcon.Error);
            return false;
        }

        return await FinishAsync(cd, choices, result, entry, folder, name, automated, quiet);
    }

    /// <summary>Shows the CD screen (with the automation countdown if it is on) and returns what was chosen.</summary>
    private async Task<(CdRipChoices? Choices, bool TookOver)> ShowCdScreenAsync(CdDriveState cd, CdMetadata meta, bool automated,
                                                                                 CancellationToken ct)
    {
        Indeterminate = false;
        var review = new CdReviewViewModel(cd, meta, _folders.MusicFolder, _dialogs);
        if (AutoNext || automated) review.LockEject();
        if (automated) review.StartAutomation();
        Review = review;
        State = RipState.Reviewing;
        DriveState = $"CD read: {Text.Plural(cd.Tracks.Count, "track")}";
        if (!automated)
            _notify.Notify("CD ready", (meta.Found ? $"{review.Album} by {review.Artist}" : "The CD") + " has been read. Choose what to rip and click RIP.");

        _automationProblem = null;
        CdRipChoices? choices;
        using (ct.Register(review.Cancel))
        {
            if (automated) await RunAutomationCountdownAsync(review, ct);
            choices = await review.Result;
        }
        Review = null;
        return (choices, review.TookOver);
    }

    /// <summary>Never mix two rips in one folder: a CD ripped again (or two unknown CDs) gets "Album (2)".</summary>
    private static string UniqueAlbumFolder(string folder)
    {
        if (!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, "*.flac").Any()) return folder;
        var unique = folder;
        for (int k = 2; Directory.Exists(unique); k++) unique = $"{folder} ({k})";
        return unique;
    }

    /// <summary>Runs cyanrip for the chosen tracks with live progress.</summary>
    private async Task<(CdRipResult Result, HistoryEntry Entry)> RipAsync(CdDriveState cd, CdMetadata meta, CdRipChoices choices,
                                                                          string folder, string name, CancellationToken ct)
    {
        _stopping = false;
        _ripNumber++;
        _lastRipName = name;
        _lastRipFolder = folder;
        State = RipState.Ripping;
        DriveState = $"Ripping {cd.Letter}";
        ButtonText = "Abort rip";
        Status = $"Ripping {name}";
        Detail = "Starting cyanrip…";
        Warning = "";
        Progress = 0;
        Stats.Start(cd.Tracks.Where(t => choices.Tracks.Contains(t.Number)).Sum(t => t.Seconds), () => Progress);
        Log.Add($"Ripping {Text.Plural(choices.Tracks.Count, "track")} to {folder}");

        var entry = _history.BeginRip(meta.Found ? choices.Album : cd.Label, name, folder, choices.Tracks.Count);

        //the drive's read offset: the one set in Settings, else the known one for this drive, else 0.
        var known = Cyanrip.KnownOffset(meta.DriveModel);
        int offset = _settings.Current.CdOffset ?? known ?? 0;
        Log.Add(_settings.Current.CdOffset == null && known == null
            ? "The drive's read offset isn't known, so 0 is used."
            : $"Drive read offset: {offset:+0;-0;0} samples.");

        var session = new CdRipSession(_runners.Cyanrip, () => _tools.Cyanrip);
        try
        {
            var result = await session.RunAsync(new CdRipRequest
                {
                    Drive = cd.Letter, Folder = folder, Tracks = choices.Tracks, Metadata = choices.Metadata,
                    TotalTracks = cd.Tracks.Count, Offset = offset,
                },
                p => OnProgress(p, entry),
                ct,
                Log.Add);
            try
            {
                File.WriteAllLines(_mode.LastCdRipLog, session.Log);
            }
            catch
            {
            }
            return (result, entry);
        }
        catch
        {
            _history.EndUnfinishedRip(entry, Array.Empty<string>(), 0);
            throw;
        }
        finally
        {
            Stats.Stop();
        }
    }

    /// <summary>Ejects, records and reports a successful rip. True if the app should wait for the next CD.</summary>
    private async Task<bool> FinishAsync(CdDriveState cd, CdRipChoices choices, CdRipResult result, HistoryEntry entry,
                                         string folder, string name, bool automated, bool quiet)
    {
        Log.Add($"Finished: {Text.Plural(result.Files.Count, "track")}.");
        bool ejected = false;
        if (choices.Eject)
        {
            Detail = "Opening the tray…";
            ejected = await Task.Run(() => _drives.EjectCdDrive(cd.Letter));
            DriveState = ejected ? "Tray is open" : "Done (couldn't open the tray)";
        }
        else
        {
            _simulators.Cd?.CdFinished();
        }

        _history.FinishRip(entry, result.Files, result.Problems);
        bool again = AutoNext || automated;
        bool problems = result.Problems > 0;

        State = RipState.Idle;
        ButtonText = "Rip another CD";
        Indeterminate = false;
        Progress = 100;
        Status = problems ? "Finished, with warnings." : "All done!";
        Detail = $"Saved to {folder}" + (choices.Eject && !ejected ? "   (couldn't open the tray automatically)" : "");
        Stats.ShowFinished();
        Warning = "";

        var next = !again ? "Click \"Rip another CD\" when you're ready for the next one."
                 : ejected ? "Insert the next CD." : "Take the CD out and insert the next one.";
        _notify.Notify($"Finished ripping {name}" + (problems ? " (with warnings)" : ""), next);

        if (problems && quiet)
            _carryWarning = $"{name} finished, but cyanrip reported {Text.Plural(result.Problems, "read problem")}. Log: {_mode.LastCdRipLog}";
        else if (problems)
            _dialogs.Show($"Saved {Text.Plural(result.Files.Count, "track")} to {folder}.\n\n" +
                          $"cyanrip reported {Text.Plural(result.Problems, "read problem")}, so some tracks may have clicks or gaps.\n\n" +
                          $"Full log: {_mode.LastCdRipLog}", "Finished with warnings", DialogIcon.Warning);
        else if (!again)
            _dialogs.Show($"Saved {Text.Plural(result.Files.Count, "track")} to:\n\n{folder}", "Finished");

        if (!again) return false;
        _watcher.Ignore = cd.Signature;   //never take the finished CD for the next one
        return true;
    }

    /// <summary>Shows cyanrip's progress on the tab and in the history.</summary>
    private void OnProgress(CdRipProgress p, HistoryEntry entry)
    {
        if (State != RipState.Ripping || _stopping) return;
        Indeterminate = false;
        Progress = p.Percent;
        var title = _trackTitles.TryGetValue(p.TrackNumber, out var t) && t.Length > 0 ? $"  {t}" : "";
        Stats.TrackText = $"Track {p.TrackIndex} of {p.TrackCount}  ·  {p.TrackNumber:00}{title}  ({p.TrackPercent:0}%)";
        Detail = $"{p.Percent:0.0}% complete";
        _history.UpdateRip(entry, p.Percent,
            p.TrackCount == 1 ? $"Ripping {p.Percent:0}%" : $"Track {p.TrackIndex} of {p.TrackCount} · {p.Percent:0}%", 0);
    }

    /// <summary>Counts down for 3 seconds on the CD screen, then presses RIP (unless someone takes over).</summary>
    private async Task RunAutomationCountdownAsync(CdReviewViewModel review, CancellationToken ct)
    {
        const int steps = 30;   //3 seconds
        var what = review.FoundNames ? $"ripping every track of {review.Album}" : "ripping every track (no names found, so tracks are numbered)";
        try
        {
            for (int i = 0; i < steps; i++)
            {
                if (!review.IsAutomated || review.Result.IsCompleted) return;
                int left = (int)Math.Ceiling((steps - i) / 10.0);
                review.AutomationText = $"Automation mode: {what}. Starting in {left} s…";
                review.AutomationProgress = i * 100.0 / steps;
                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException)
        {
            review.Cancel();
            return;
        }
        if (!review.IsAutomated || review.Result.IsCompleted) return;
        review.AutomationProgress = 100;
        review.AutomationText = "Automation mode: starting the rip…";

        var problem = review.Validate();
        if (problem == null)
        {
            try
            {
                Directory.CreateDirectory(review.MusicRoot.Trim());
            }
            catch (Exception ex)
            {
                problem = $"Couldn't create the music folder {review.MusicRoot}: {ex.Message}";
            }
        }
        if (problem != null)
        {
            _automationProblem = problem;
            review.Cancel();
            return;
        }
        review.Confirm();
    }

    /// <summary>Automation mode: reports a CD that couldn't be done, ejects it and moves on.</summary>
    private async Task SkipCdAsync(CdDriveState cd, string title, string reason)
    {
        var first = Text.FirstLine(reason);
        Log.Add($"{title}: {first}");
        _notify.Notify(title, first + " The CD was ejected and skipped.");
        _carryWarning = $"{title}: {first} It was ejected and skipped.";
        Status = $"{title}.";
        Detail = "Ejecting it and moving on to the next CD…";
        await Task.Run(() => _drives.EjectCdDrive(cd.Letter));
        _watcher.Ignore = cd.Signature;
    }

    /// <summary>Back to idle with a message.</summary>
    private void Reset(string message, bool keepStats = false)
    {
        State = RipState.Idle;
        ButtonText = "Start";
        Indeterminate = false;
        Progress = 0;
        Status = message;
        Detail = "";
        Warning = "";
        if (keepStats)
        {
            Stats.Stop();
            Stats.RemainingText = "—";
        }
        else Stats.Hide();
    }

    /// <summary>Back to idle, with a notification and an error message.</summary>
    private void Fail(string message)
    {
        Reset("Something went wrong.");
        _notify.Notify("Something went wrong", Text.FirstLine(message));
        _dialogs.Show(message, "Error", DialogIcon.Error);
    }

    #endregion
}
