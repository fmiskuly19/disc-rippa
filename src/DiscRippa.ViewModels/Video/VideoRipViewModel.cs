namespace DiscRippa.ViewModels.Video;

/// <summary>
/// The DVD / Blu-ray tab: wait for a disc, read it, show the disc screen, rip what was chosen with live progress, then
/// (optionally) wait for the next disc. In automation mode the disc screen counts down and rips the main movie by itself.
/// </summary>
public sealed partial class VideoRipViewModel : ObservableObject
{
    #region Fields

    private readonly DiscWatcher _watcher;
    private readonly ToolRunners _runners;
    private readonly IToolPaths _tools;
    private readonly ISettingsService _settings;
    private readonly RunMode _mode;
    private readonly AppFolders _folders;
    private readonly PresetService _presets;
    private readonly CompressQueue _queue;
    private readonly HistoryStore _history;
    private readonly IDriveService _drives;
    private readonly ICdDriveProbe _cdProbe;
    private readonly Simulators _simulators;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notify;
    private readonly ToolPicker _picker;
    private readonly Navigation _navigation;
    private CancellationTokenSource? _cts;
    private RipSession? _session;
    private Ticker? _idleTicker;
    private bool _stopping;                   //abort was confirmed; ignore late progress lines
    private int _ripNumber;                   //which rip is running, to tell if it ended while a question was open
    private string _lastRipName = "The disc";
    private string _lastRipFolder = "";
    private string _carryWarning = "";        //a skipped disc's message, kept on screen while waiting for the next
    private string? _automationProblem;       //why automation stopped (e.g. not enough space)
    private bool _checkingDrives;
    private bool _checkingCd;
    private bool _syncingSettings;
    private string? _driveSignature;          //what the drive was doing last time we looked
    private DriveStatus? _lastDrive;          //what MakeMKV last said is in the drive

    #endregion

    #region Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsReviewing), nameof(ShowStatusCard), nameof(CanChangeAutomation), nameof(PrimaryEnabled))]
    private RipState state = RipState.Idle;

    [ObservableProperty] private string status = "Insert a disc and click Start.";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool indeterminate;

    /// <summary>The big button: Start / Read this disc / Cancel / Abort rip / Rip another disc.</summary>
    [ObservableProperty] private string buttonText = "Start";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string warning = "";

    public bool HasWarning => Warning.Length > 0;

    /// <summary>The disc screen while it's showing; null otherwise.</summary>
    [ObservableProperty] private DiscReviewViewModel? review;

    /// <summary>"HL-DT-ST BD-RE BP50NB40 (G:)". Used by the drive box in debug mode, where each side has its own pretend drive.</summary>
    [ObservableProperty] private string driveTitle = "Looking for your disc drive…";

    /// <summary>"No disc in the drive", "Reading the disc…", "Ripping: THE_MOVIE" and so on.</summary>
    [ObservableProperty] private string driveState = "";

    /// <summary>The live numbers under the progress bar.</summary>
    public VideoRipStats Stats { get; } = new();

    /// <summary>Everything MakeMKV says during the rip.</summary>
    public LogViewModel Log { get; } = new();

    public bool IsBusy => State != RipState.Idle;
    public bool IsReviewing => State == RipState.Reviewing;
    public bool ShowStatusCard => State != RipState.Reviewing;

    /// <summary>The "MakeMKV not found" banner.</summary>
    public bool MakeMkvMissing => _tools.MakeMkv == null;

    /// <summary>
    /// Start needs MakeMKV, and is off while a music CD is in (this side can't rip it). Once something is running the
    /// button is Cancel / Abort, which always works.
    /// </summary>
    public bool PrimaryEnabled => _tools.MakeMkv != null && !(HasMusicCd && State == RipState.Idle);

    /// <summary>False while the Music CD side is showing: the drive isn't polled through MakeMKV then.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Automation mode: discs are read, ripped (main movie), queued for compression and ejected with no clicks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoNextEnabled))]
    private bool automation;

    /// <summary>"Keep Ripping!": when this disc is done, eject it and wait for the next one.</summary>
    [ObservableProperty] private bool autoNext;

    /// <summary>The "Keep Ripping!" box is implied (and locked) while automation mode is on.</summary>
    public bool AutoNextEnabled => !Automation && !HasMusicCd;

    /// <summary>
    /// Automation mode can't be switched while a disc is being read, shown or ripped, only while idle or waiting for one
    /// (so a job never changes rules half-way through).
    /// </summary>
    public bool CanChangeAutomation => (State is RipState.Idle or RipState.Waiting) && !HasMusicCd;

    /// <summary>
    /// A music CD is in a drive. MakeMKV doesn't see music CDs at all, so without this the tab would just keep saying
    /// "insert a disc". Shown as a warning with a button to switch to Music CD mode.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoNextEnabled), nameof(CanChangeAutomation), nameof(PrimaryEnabled))]
    private bool hasMusicCd;

    #endregion

    #region Constructors

    /// <summary>Creates the DVD / Blu-ray tab.</summary>
    public VideoRipViewModel(DiscWatcher watcher, ToolRunners runners, IToolPaths tools, ISettingsService settings, RunMode mode,
                             AppFolders folders, PresetService presets, CompressQueue queue, HistoryStores history,
                             IDriveService drives, ICdDriveProbe cdProbe, Simulators simulators,
                             IDialogService dialogs, INotificationService notify, ToolPicker picker,
                             Navigation navigation)
    {
        _navigation = navigation;
        _watcher = watcher;
        _runners = runners;
        _tools = tools;
        _settings = settings;
        _mode = mode;
        _folders = folders;
        _presets = presets;
        _queue = queue;
        _history = history.Videos;
        _drives = drives;
        _cdProbe = cdProbe;
        _simulators = simulators;
        _dialogs = dialogs;
        _notify = notify;
        _picker = picker;

        automation = settings.Current.AutomationMode;
        autoNext = settings.Current.AutoNext;

        _watcher.DrivesChanged += OnDrivesChanged;
        _drives.DriveNamesChanged += () =>
        {
            if (State == RipState.Idle) _ = CheckDrivesAsync(quiet: true);
        };
        _tools.Changed += () => OnPropertyChanged(nameof(MakeMkvMissing));
        _tools.Changed += () => OnPropertyChanged(nameof(PrimaryEnabled));
        _settings.Saved += SyncFromSettings;
    }

    #endregion

    #region Commands

    /// <summary>
    /// The big button. It stays clickable while a rip runs (that's when it means "Abort rip"), hence concurrent
    /// executions: a plain async command would grey itself out until the rip it started had finished.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PrimaryAsync()
    {
        switch (State)
        {
            case RipState.Idle:
                await StartAsync();
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

    /// <summary>Lets the user pick MakeMKV, then looks at the drive again.</summary>
    [RelayCommand]
    private async Task LocateMakeMkvAsync()
    {
        if (!_picker.LocateMakeMkv()) return;
        _driveSignature = null;
        await StartWatchingAsync();
    }

    #endregion

    #region Methods

    /// <summary>Called when auto next changes.</summary>
    partial void OnAutoNextChanged(bool value)
    {
        if (_syncingSettings) return;
        _settings.Current.AutoNext = value;
        _settings.Save();
    }

    /// <summary>Called when automation changes.</summary>
    partial void OnAutomationChanged(bool value)
    {
        if (_syncingSettings) return;
        _settings.Current.AutomationMode = value;
        _settings.Save();

        if (!value) Review?.TakeOver();   //a countdown in progress becomes a normal, manual disc screen
        if (State == RipState.Idle)
        {
            //look at the drive now: with automation on, a disc that's already in gets started straight away.
            _driveSignature = null;
            _ = CheckDrivesAsync(quiet: true);
        }
    }

    /// <summary>The Settings window may have changed these; follow it without saving again.</summary>
    private void SyncFromSettings()
    {
        _syncingSettings = true;
        try
        {
            AutoNext = _settings.Current.AutoNext;
            Automation = _settings.Current.AutomationMode;
        }
        finally
        {
            _syncingSettings = false;
        }
    }

    /// <summary>Called when has music cd changes.</summary>
    partial void OnHasMusicCdChanged(bool value)
    {
        if (State == RipState.Idle)
        {
            _driveSignature = null;   //redo the idle message either way
            if (value) ShowMusicCdPrompt();
            else _ = CheckDrivesAsync(quiet: true);
        }
        else if (State == RipState.Waiting)
        {
            if (value)
            {
                Status = "That's a music CD.";
                Detail = "DVD / Blu-ray mode can't rip it. Insert a DVD or Blu-ray instead, or switch to Music CD.";
            }
            else
            {
                Status = "Waiting for a disc… insert one now.";
                Detail = Automation ? "Automation mode: it will be ripped and ejected automatically." : "";
            }
        }
    }

    /// <summary>Checks whether a music CD is in the drive (MakeMKV can't see those).</summary>
    private async Task CheckForMusicCdAsync()
    {
        //debug mode has separate pretend drives for each side, so a pretend music CD mustn't show up here.
        if (_mode.IsDebug || !IsActive || _checkingCd) return;
        _checkingCd = true;
        try
        {
            var drives = await Task.Run(() => _cdProbe.GetDrives());
            HasMusicCd = drives.Any(d => d.IsReady && d.IsAudioCd);
        }
        catch
        {
        }
        finally
        {
            _checkingCd = false;
        }
    }

    /// <summary>The message shown while a music CD is in the drive.</summary>
    private void ShowMusicCdPrompt()
    {
        Status = "That's a music CD.";
        Detail = "DVD / Blu-ray mode can't rip it. Insert a DVD or Blu-ray instead, or switch to Music CD.";
        DriveState = "Music CD inserted";
        ButtonText = "Start";
    }

    /// <summary>
    /// Looks at the drive now, then keeps looking every few seconds while idle, so the message and the button always
    /// match what's in the drive (disc in, tray open, empty).
    /// </summary>
    public async Task StartWatchingAsync()
    {
        _idleTicker ??= Ticker.Start(_mode.IdlePollInterval, async () =>
        {
            if (State is RipState.Idle or RipState.Waiting) await CheckForMusicCdAsync();
            if (State == RipState.Idle) await CheckDrivesAsync(quiet: true);
        });
        await CheckForMusicCdAsync();
        await CheckDrivesAsync(quiet: false);
    }

    /// <summary>Called when this side comes to the front (true) or the Music CD side does (false).</summary>
    public async Task SetActiveAsync(bool active)
    {
        IsActive = active;
        if (!active || State != RipState.Idle) return;
        _driveSignature = null;
        await CheckForMusicCdAsync();
        await CheckDrivesAsync(quiet: true);
    }

    /// <summary>Stops waiting for a disc (so the app can switch to Music CD mode). Returns once it's idle.</summary>
    public async Task StopWaitingAsync()
    {
        if (State != RipState.Waiting) return;
        _cts?.Cancel();
        for (int i = 0; i < 50 && State != RipState.Idle; i++) await Task.Delay(50);
    }

    /// <summary>Asks MakeMKV what is in the drive (only while idle).</summary>
    private async Task CheckDrivesAsync(bool quiet)
    {
        if (_tools.MakeMkv == null)
        {
            DriveTitle = "Disc drive unknown";
            DriveState = "MakeMKV is needed to talk to the drive.";
            return;
        }
        if (State != RipState.Idle || _checkingDrives || !IsActive) return;
        _checkingDrives = true;
        if (!quiet)
        {
            DriveTitle = "Looking for your disc drive…";
            DriveState = "";
        }
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await _watcher.CheckDrivesAsync(limit.Token);
        }
        catch
        {
            if (!quiet)
            {
                DriveTitle = "Couldn't check the disc drive";
                DriveState = "It will be checked again when you click Start.";
            }
        }
        finally
        {
            _checkingDrives = false;
        }
    }

    /// <summary>Updates the drive text and the idle message, and starts automation when a disc goes in.</summary>
    private void OnDrivesChanged(IReadOnlyList<DriveStatus> drives)
    {
        if (State.IsWorking()) return;   //reading, the disc screen and ripping set their own text

        var drive = drives.FirstOrDefault(d => d.State == DriveStatus.Inserted) ?? drives.FirstOrDefault();
        _lastDrive = drive;
        if (drive == null)
        {
            DriveTitle = "No disc drive found";
            DriveState = "Connect a DVD or Blu-ray drive.";
        }
        else
        {
            var more = drives.Count > 1 ? $"   (+{Text.Plural(drives.Count - 1, "more drive")})" : "";
            DriveTitle = _drives.DriveName(drive.Device, drive.Title) + more;
            DriveState = drive.State == DriveStatus.Inserted && drive.DiscName.Length > 0
                ? $"Disc inserted: {FileNames.CleanDiscName(drive.DiscName)}"
                : drive.StateText;
        }

        bool musicCdShowing = HasMusicCd && (drive?.State != DriveStatus.Inserted || drive.DiscName.Length == 0);
        if (musicCdShowing) DriveState = "Music CD inserted";

        //when idle, the main message follows the drive — but only when what's in the drive changes, so a result
        //like "All done!" or "Cancelled" stays put until something actually happens.
        var signature = drive == null ? "none" : $"{drive.Index}|{drive.State}|{drive.DiscName}";
        bool changed = signature != _driveSignature;
        _driveSignature = signature;
        if (State != RipState.Idle || !changed) return;

        if (musicCdShowing) ShowMusicCdPrompt();
        else if (Automation && drive?.State == DriveStatus.Inserted) _ = StartAsync();   //a disc went in, so off we go
        else ShowIdlePrompt(drive);
    }

    /// <summary>The message to show while nothing is happening, depending on what is in the drive.</summary>
    private void ShowIdlePrompt(DriveStatus? drive)
    {
        ButtonText = "Start";
        switch (drive?.State)
        {
            case null:
                Status = "No disc drive found.";
                Detail = "Connect a DVD or Blu-ray drive. It's picked up automatically.";
                break;
            case DriveStatus.Inserted:
                var name = drive.DiscName.Length > 0 ? FileNames.CleanDiscName(drive.DiscName) : "A disc";
                Status = $"{name} is in the drive.";
                Detail = Automation
                    ? "Automation mode is on. Click below to rip it now, or put in a different disc."
                    : "Click below to read it and choose what to rip.";
                ButtonText = "Read this disc";
                break;
            case DriveStatus.Loading:
                Status = "The drive is loading a disc…";
                Detail = "You can click Start now. The disc is read as soon as it's ready.";
                break;
            default:
                Status = Automation ? "Automation mode is on: insert a disc." : "Insert a disc and click Start.";
                Detail = Automation
                    ? "It will be read, the main movie ripped and queued for compression, and the disc ejected. No clicks needed."
                    : "You can also click Start first, then insert the disc.";
                break;
        }
    }

    /// <summary>Asks before stopping a rip, then stops it.</summary>
    private void ConfirmAbort()
    {
        int rip = _ripNumber;
        if (!_dialogs.Confirm("Stop ripping this disc now?\n\nThe unfinished file is deleted. Titles that already finished are kept.",
                              "Abort rip?", defaultNo: true))
            return;

        if (State == RipState.Ripping && rip == _ripNumber)
        {
            _stopping = true;
            Status = "Stopping the rip…";
            Detail = "Removing the unfinished file.";
            Log.Add("Abort requested — stopping MakeMKV.");
            _cts?.Cancel();
            return;
        }

        //MakeMKV kept going while the question was open and finished in the meantime.
        _dialogs.Show($"The rip finished while that question was open, so there was nothing left to stop.\n\n" +
                      $"{_lastRipName} was saved in {_lastRipFolder} and is listed on the Ripped Discs tab.\n" +
                      "If you don't want it, use Open folder on that tab and delete the file.",
                      "Already finished");
    }

    /// <summary>Start (or "Read this disc"): whatever is in the drive is fair game, even a disc that was just skipped.</summary>
    private async Task StartAsync()
    {
        try
        {
            _watcher.Ignore = null;
            await RunAsync(afterRip: false);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            Fail(ex.Message);
        }
    }

    /// <summary>Called when the app closes: stop MakeMKV and remove the half-written file.</summary>
    public void Shutdown()
    {
        Review?.Cancel();
        _cts?.Cancel();
        _idleTicker?.Dispose();
        Stats.Stop();
        if (_session != null)
        {
            Thread.Sleep(500);   //let MakeMKV release the file
            _session.RemovePartialFiles();
        }
    }

    /// <summary>Waits for a disc, reads it, rips it, and repeats while "Keep Ripping!" is on.</summary>
    private async Task RunAsync(bool afterRip)
    {
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
            Status = afterRip
                ? "Waiting for the next disc… take the finished one out and insert the next."
                : "Waiting for a disc… insert one now.";

            //a disc that's already in goes straight to reading: no "insert one now" while MakeMKV gets going.
            if (!afterRip && !HasMusicCd && _lastDrive?.State == DriveStatus.Inserted)
            {
                var name = _lastDrive.DiscName.Length > 0 ? FileNames.CleanDiscName(_lastDrive.DiscName) : "the disc";
                Status = $"Reading {name}…";
                Detail = "Starting MakeMKV. Reading a disc can take a minute or two.";
            }

            DiscFound found;
            var reading = new ReadFeedback(this);
            try
            {
                found = await _watcher.WaitForDiscAsync(label =>
                    {
                        State = RipState.Scanning;
                        DriveState = $"Reading the disc: {FileNames.CleanDiscName(label)}";
                        Status = $"Reading {FileNames.CleanDiscName(label)}…";
                        reading.Start();
                    },
                    ct,
                    reading.SetMessage,
                    percent =>
                    {
                        Indeterminate = false;
                        Progress = percent;
                    });
            }
            catch (OperationCanceledException)
            {
                Reset("Cancelled. Click Start when you're ready.");
                return;
            }
            catch (DiscReadException ex) when (Automation)
            {
                //automation mode: report it, eject the disc, and carry on with the next one.
                await SkipDiscAsync(ex.Label, ex.Device, $"Couldn't read {FileNames.CleanDiscName(ex.Label)}", ex.Message);
                afterRip = true;
                continue;
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
                return;
            }
            finally
            {
                reading.Dispose();
            }

            if (!await RipDiscAsync(found, ct)) return;
            afterRip = true;
        }
    }

    /// <summary>Shows the disc screen, rips what was chosen, and returns true if the app should wait for the next disc.</summary>
    private async Task<bool> RipDiscAsync(DiscFound found, CancellationToken ct)
    {
        bool automated = Automation;
        var choices = await ShowDiscScreenAsync(found, automated, ct);
        bool quiet = automated && !choices.TookOver;   //no pop-ups while automation runs this disc
        if (choices.Choices is not RipChoices chosen)
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

        if (!_mode.IsDebug && chosen.Compress)   //test runs shouldn't change real settings
        {
             _settings.Current.CompressToMkv = chosen.SaveAsMkv;   //offered again next time
            _settings.Save();
        }

        var (result, entry) = await RipAsync(found, chosen, ct);

        if (result.Cancelled)
        {
            var text = "Rip aborted.";
            if (result.RemovedPartialFiles > 0) text += " The unfinished file was removed.";
            if (result.Files.Count > 0) text += $" {Text.Plural(result.Files.Count, "finished title")} kept.";
            Log.Add(text);
            _history.EndUnfinishedRip(entry, result.Files, result.Problems.Count);
            Reset(text, keepStats: true);
            return false;
        }

        if (!result.Success)
        {
            _history.EndUnfinishedRip(entry, result.Files, result.Problems.Count);
            if (quiet)
            {
                Log.Add("The rip failed. Automation mode: skipping this disc.");
                await SkipDiscAsync(found.DiscLabel, found.Device, $"Ripping {chosen.Name} failed", result.Error ?? "");
                return true;
            }
            Log.Add("The rip failed.");
            Reset("Something went wrong with that disc.", keepStats: true);
            _notify.Notify("Rip failed", Text.FirstLine(result.Error));
            _dialogs.Show($"{result.Error}\n\nFull log: {_mode.LastRipLog}", "Rip failed", DialogIcon.Error);
            return false;
        }

        return await FinishAsync(found, chosen, result, entry, automated, quiet);
    }

    /// <summary>Shows the disc screen (with the automation countdown if it is on) and returns what was chosen.</summary>
    private async Task<DiscScreenOutcome> ShowDiscScreenAsync(DiscFound found, bool automated, CancellationToken ct)
    {
        Indeterminate = false;
        var review = new DiscReviewViewModel(found, _folders.MovieFolder, _presets.Choices.ToList(), _presets.DefaultPreset,
                                             _tools.HandBrake != null, keepOriginal: _settings.Current.KeepMkv,
                                             saveAsMkv: _settings.Current.CompressToMkv, dialogs: _dialogs,
                                             changeFolder: async () => await _navigation.OpenSettingsAsync(SettingsViewModel.MovieFolderSetting) ? _folders.MovieFolder : null);
        if (AutoNext || automated) review.LockEject();   //"Keep Ripping!" needs the tray open
        if (automated) review.StartAutomation();
        Review = review;
        State = RipState.Reviewing;
        DriveState = $"Disc read: {found.DiscLabel}";
        if (!automated) _notify.Notify("Disc ready", $"{review.DiscTitle} has been read. Choose what to rip and click RIP.");

        _automationProblem = null;
        RipChoices? choices;
        using (ct.Register(review.Cancel))
        {
            if (automated) await RunAutomationCountdownAsync(review, ct);
            choices = await review.Result;
        }
        Review = null;
        return new DiscScreenOutcome(choices, review.TookOver);
    }

    /// <summary>Runs MakeMKV for the chosen titles with live progress.</summary>
    private async Task<(RipResult Result, HistoryEntry Entry)> RipAsync(DiscFound found, RipChoices chosen, CancellationToken ct)
    {
        var titles = chosen.Titles;
        _stopping = false;
        _ripNumber++;
        _lastRipName = chosen.Name;
        _lastRipFolder = chosen.Folder;
        State = RipState.Ripping;
        DriveState = $"Ripping: {found.DiscLabel}";
        ButtonText = "Abort rip";
        Status = "Starting rip…";
        Detail = "";
        Warning = "";
        Progress = 0;
        Log.Clear();
        _session = new RipSession(_runners.MakeMkv, () => _tools.MakeMkv);
        var session = _session;
        Stats.Start(titles.Sum(t => t.Size), () => session.BytesWritten(), () => Progress);
        Log.Add($"Ripping {titles.Count} title(s) from {found.DiscLabel} to {chosen.Folder}");

        //the disc shows up on the Ripped Discs tab straight away, with live progress.
        var entry = _history.BeginRip(FileNames.CleanDiscName(found.DiscLabel), chosen.Name, chosen.Folder, titles.Count);
        int problemCount = 0;
        try
        {
            Directory.CreateDirectory(chosen.Folder);   //several titles get a folder of their own
            var result = await session.RunAsync(
                new RipRequest { DriveIndex = found.DriveIndex, Titles = titles, DestDir = chosen.Folder, BaseName = chosen.Name },
                p => OnRipProgress(p, chosen.Name, entry, problemCount),
                count =>
                {
                    problemCount = count;
                    if (_stopping) return;
                    Warning = $"MakeMKV has reported {Text.Plural(count, "problem")} reading this disc " +
                              "(scratches or dirt?). The video may have glitches. Click Abort rip to stop.";
                },
                ct,
                Log.Add);
            try
            {
                File.WriteAllLines(_mode.LastRipLog, result.Log);
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
            _session = null;
        }
    }

    /// <summary>Ejects, queues, records and reports a successful rip. True if the app should wait for the next disc.</summary>
    private async Task<bool> FinishAsync(DiscFound found, RipChoices chosen, RipResult result, HistoryEntry entry,
                                         bool automated, bool quiet)
    {
        Log.Add($"Finished: {string.Join(", ", result.Files)}");
        bool ejected = false;
        if (chosen.Eject)
        {
            Detail = "Opening the tray…";
            entry.SetLiveStatus("Finishing…");
            ejected = await Task.Run(() => _drives.EjectVideoDrive(found.Device));
            DriveState = ejected ? "Tray is open" : $"Done with {found.DiscLabel} (couldn't open the tray)";
        }
        else
        {
            _simulators.Video?.DiscFinished();   //debug mode: pretend the disc is taken out by hand
            DriveState = $"Done with {found.DiscLabel}";
        }

        bool queued = false;
        if (chosen.Compress && _tools.HandBrake != null && result.Files.Count > 0)
        {
            var compressedFolder = _folders.CompressedFolderFor(chosen.Folder, chosen.SaveFolder ?? chosen.Folder);
            _queue.Add(result.Files.Select(f => Path.Combine(chosen.Folder, f)), compressedFolder, chosen.Preset,
                       deleteOriginal: !chosen.KeepOriginal, saveAsMkv: chosen.SaveAsMkv);
            queued = true;
        }

        if (result.Files.Count > 0) _history.FinishRip(entry, result.Files, result.Problems.Count);
        else _history.EndUnfinishedRip(entry, result.Files, result.Problems.Count);

        bool again = (AutoNext || automated) && result.Files.Count > 0;
        bool problems = result.Problems.Count > 0;

        State = RipState.Idle;
        ButtonText = "Rip another disc";
        Indeterminate = false;
        Progress = 100;
        Status = problems ? "Finished, with warnings." : "All done!";
        Detail = $"Saved to {chosen.Folder}" + (chosen.Eject && !ejected ? "   (couldn't open the tray automatically)" : "");
        Stats.ShowFinished();
        Warning = "";

        string next = !again
            ? "Click \"Rip another disc\" when you're ready for the next one."
            : ejected ? "Insert the next disc." : "Take the disc out and insert the next one.";
        _notify.Notify($"Finished ripping {chosen.Name}" + (problems ? " (with warnings)" : ""), next);

        var summary = "Saved:\n\n" + string.Join("\n", result.Files);
        if (queued)
            summary += chosen.KeepOriginal
                ? "\n\nA compressed copy will be saved alongside it (see the Compress Queue tab)."
                : "\n\nA compressed copy will be made (see the Compress Queue tab), and the MKV deleted once it's done.";

        if (problems && quiet)
        {
            _carryWarning = $"{chosen.Name} finished, but MakeMKV reported read problems, so the video may have glitches. Log: {_mode.LastRipLog}";
        }
        else if (problems)
        {
            summary += "\n\nMakeMKV reported problems while reading the disc, so the rip may contain glitches:\n- "
                       + string.Join("\n- ", result.Problems.Take(5)) + $"\n\nFull log: {_mode.LastRipLog}";
            _dialogs.Show(summary, "Finished with warnings", DialogIcon.Warning);
        }
        else if (!again)
        {
            _dialogs.Show(summary, "Finished");
        }

        if (!again) return false;
        _watcher.Ignore = new DiscKey(found.DiscLabel, found.Device);   //never mistake this disc for the next one
        return true;
    }

    /// <summary>Shows MakeMKV's progress on the tab and in the history.</summary>
    private void OnRipProgress(RipProgress p, string name, HistoryEntry entry, int problemCount)
    {
        if (State != RipState.Ripping || _stopping) return;

        var titlePart = p.TitleCount == 1 ? "" : $"Title {p.TitleNumber} of {p.TitleCount}  ·  ";
        Stats.StageText = p.Stage.Length > 0 ? $"{titlePart}{p.Stage}  ({p.StagePercent:0}%)" : titlePart.TrimEnd(' ', '·');
        Stats.StepText = p.Step.Length > 0 ? $"Current step: {p.Step}  ({p.StepPercent:0}%)" : "";

        if (p.Saving)
        {
            Stats.SavingAt(p.Percent, p.StagePercent);
            Indeterminate = false;
            Progress = p.Percent;
            Status = p.TitleCount == 1
                ? $"Ripping {name}  ({p.Duration})"
                : $"Ripping title {p.TitleNumber} of {p.TitleCount}  ({p.Duration})";
            Detail = $"{p.Percent:0.0}% complete";
            _history.UpdateRip(entry, p.Percent,
                p.TitleCount == 1 ? $"Ripping {p.Percent:0}%" : $"Title {p.TitleNumber} of {p.TitleCount} · {p.Percent:0}%",
                problemCount);
        }
        else
        {
            //MakeMKV is still opening / scanning the disc; the real progress hasn't started yet.
            Indeterminate = p.Percent <= 0;
            Progress = p.Percent;
            Status = $"Preparing to rip {name}…";
            Detail = p.Stage.Length > 0 ? $"MakeMKV: {p.Stage}" : "MakeMKV is getting ready.";
            _history.UpdateRip(entry, p.Percent, "Preparing…", problemCount);
        }
    }

    /// <summary>
    /// Shows the locked disc screen for 3 seconds with a visible countdown, then presses RIP.
    /// "Let me choose" (TakeOver) or Cancel stops the countdown.
    /// </summary>
    private async Task RunAutomationCountdownAsync(DiscReviewViewModel review, CancellationToken ct)
    {
        var what = review.Compress
            ? "ripping the main movie, then compressing it (Auto)"
            : "ripping the main movie (HandBrakeCLI not found, so no compression)";
        const int steps = 30;   //3 seconds
        try
        {
            for (int i = 0; i < steps; i++)
            {
                if (!review.IsAutomated || review.Result.IsCompleted) return;
                int secondsLeft = (int)Math.Ceiling((steps - i) / 10.0);
                review.AutomationText = $"Automation mode: {what}. Starting in {secondsLeft} s…";
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

        //the checks a person would get as questions. In automation they stop the run instead.
        var problem = review.Validate();
        if (problem == null)
        {
            try
            {
                Directory.CreateDirectory(review.Folder.Trim());
            }
            catch (Exception ex)
            {
                problem = $"Couldn't create the folder {review.Folder}: {ex.Message}";
            }
        }
        if (problem == null && review.SpaceIsLow)
            problem = $"Not enough free space in {review.Folder} for {review.DiscTitle} (needs about {DiscReviewViewModel.Gb(review.NeededBytes)}).";
        if (problem != null)
        {
            _automationProblem = problem;
            review.Cancel();
            return;
        }
        review.Confirm();
    }

    /// <summary>Automation mode: report a disc that couldn't be done, eject it, and get ready for the next.</summary>
    private async Task SkipDiscAsync(string label, string device, string title, string reason)
    {
        var firstLine = Text.FirstLine(reason);
        Log.Add($"{title}: {firstLine}");
        _notify.Notify(title, firstLine + " The disc was ejected and skipped.");
        _carryWarning = $"{title}: {firstLine} It was ejected and skipped.";
        Status = $"{title}.";
        Detail = "Ejecting it and moving on to the next disc…";
        await Task.Run(() => _drives.EjectVideoDrive(device));
        _watcher.Ignore = new DiscKey(label, device);   //never pick the same disc up again by itself
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

    #region Nested types

    /// <summary>What came back from the disc screen: the choices (null = cancelled) and whether a person took over.</summary>
    private sealed record DiscScreenOutcome(RipChoices? Choices, bool TookOver);

    /// <summary>"Reading for 1:23…" under the status while MakeMKV reads a disc, ticking once a second.</summary>
    private sealed class ReadFeedback : IDisposable
    {
        #region Fields

        private readonly VideoRipViewModel _vm;
        private Ticker? _ticker;
        private DateTime _started;
        private string _message = "";

        #endregion

        #region Constructors

        /// <summary>Creates the feedback for this tab.</summary>
        public ReadFeedback(VideoRipViewModel vm) => _vm = vm;

        #endregion

        #region Methods

        /// <summary>Starts the "Reading for…" clock.</summary>
        public void Start()
        {
            _started = DateTime.Now;
            _message = "";
            _vm.Indeterminate = true;
            _vm.Progress = 0;
            Update();
            _ticker?.Dispose();
            _ticker = Ticker.Start(TimeSpan.FromSeconds(1), Update);
        }

        /// <summary>Shows MakeMKV's latest message under the clock.</summary>
        public void SetMessage(string message)
        {
            _message = message;
            Update();
        }

        /// <summary>Rewrites the "Reading for…" line.</summary>
        private void Update()
        {
            if (_vm.State != RipState.Scanning) return;
            var text = $"Reading for {Text.Clock(DateTime.Now - _started)}. Some discs (DVDs with complex menus especially) take a few minutes.";
            if (_message.Length > 0) text += $"\nMakeMKV: {_message}";
            _vm.Detail = text;
        }

        /// <summary>Stops the clock.</summary>
        public void Dispose()
        {
            _ticker?.Dispose();
            _ticker = null;
        }

        #endregion
    }

    #endregion
}
