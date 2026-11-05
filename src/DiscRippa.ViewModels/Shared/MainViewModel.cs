namespace DiscRippa.ViewModels.Shared;

/// <summary>How the taskbar button shows progress.</summary>
public enum TaskbarProgress { None, Indeterminate, Normal }

/// <summary>
/// The main window: the DVD / Blu-ray | Music CD switch, the tabs of each side, the drive box, the taskbar and tray
/// progress, debug mode, and what happens when the window closes.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    #region Fields

    private readonly ISettingsService _settings;
    private readonly RunMode _mode;
    private readonly PresetService _presets;
    private readonly CompressQueue _queue;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notify;
    private readonly IWindowService _windows;
    private string _trayText = "";
    private bool _debugWhenSettingsOpened;

    #endregion

    #region Properties

    public VideoRipViewModel Video { get; }

    /// <summary>Whether Settings is showing in place of everything else.</summary>
    public Navigation Navigation { get; }

    public CdRipViewModel Cd { get; }
    public DriveBoxViewModel Drive { get; }
    public QueueViewModel Queue { get; }
    public HistoryViewModel VideoHistory { get; }
    public HistoryViewModel MusicHistory { get; }
    public bool IsDebug => _mode.IsDebug;
    public string WindowTitle => IsDebug ? "DiscRippa — DEBUG MODE" : "DiscRippa";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoMode))]
    private bool isCdMode;

    public bool IsVideoMode => !IsCdMode;
    [ObservableProperty] private TaskbarProgress taskbarState;

    /// <summary>0 to 1, for the taskbar button.</summary>
    [ObservableProperty] private double taskbarValue;

    #endregion

    #region Constructors

    /// <summary>Creates the main window's view model from its parts.</summary>
    public MainViewModel(VideoRipViewModel video, CdRipViewModel cd, DriveBoxViewModel drive, QueueViewModel queue,
                         HistoryStores history, ISettingsService settings, RunMode mode, PresetService presets,
                         IDialogService dialogs, IShellService shell, INotificationService notify, IWindowService windows,
                         Navigation navigation)
    {
        Video = video;
        Cd = cd;
        Drive = drive;
        Queue = queue;
        VideoHistory = new HistoryViewModel(history.Videos, dialogs, shell);
        MusicHistory = new HistoryViewModel(history.Music, dialogs, shell);
        _settings = settings;
        _mode = mode;
        _presets = presets;
        _queue = queue.Queue;
        _dialogs = dialogs;
        _notify = notify;
        _windows = windows;
        Navigation = navigation;
        navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Navigation.IsSettingsOpen) && navigation.IsSettingsOpen)
                _debugWhenSettingsOpened = _settings.Current.DebugMode;
        };
        navigation.SettingsClosed += saved => _ = AfterSettingsAsync(saved);

        isCdMode = settings.Current.Mode == "Cd";
        drive.CdMode = isCdMode;

        video.PropertyChanged += (_, _) => UpdateProgress();
        cd.PropertyChanged += (_, _) => UpdateProgress();
        _queue.PropertyChanged += (_, _) => UpdateProgress();
    }

    #endregion

    #region Commands

    /// <summary>Switches to the DVD / Blu-ray side.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowVideoAsync() => SwitchModeAsync(cd: false);

    /// <summary>Switches to the Music CD side.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowCdAsync() => SwitchModeAsync(cd: true);

    /// <summary>The banner on the DVD / Blu-ray tab when a music CD is in: stop waiting for a disc and switch sides.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SwitchToCdAsync()
    {
        await Video.StopWaitingAsync();
        await SwitchModeAsync(cd: true);
    }

    /// <summary>The banner on the Music CD tab when a DVD / Blu-ray is in: stop waiting for a CD and switch sides.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SwitchToVideoAsync()
    {
        await Cd.StopWaitingAsync();
        await SwitchModeAsync(cd: false);
    }

    /// <summary>Shows the Settings page in place of everything else.</summary>
    [RelayCommand]
    private void OpenSettings() => _ = Navigation.OpenSettingsAsync();

    /// <summary>The "Turn off debug mode" button on the debug banner.</summary>
    [RelayCommand]
    private void TurnOffDebug() => SwitchDebugMode(false);

    #endregion

    #region Methods

    /// <summary>Looks at the drives, loads HandBrake's presets and starts the compress queue. Called once the window is up.</summary>
    public async Task StartAsync()
    {
        //HandBrake is asked straight away, alongside the drive checks (which can take a minute with a disc in).
        var presets = _presets.LoadAsync();
        await ApplyModeAsync();
        _ = Video.StartWatchingAsync();
        _ = Drive.StartAsync();
        await presets;
        _queue.Pump();
    }

    /// <summary>Switches sides, unless the other side is busy with a disc.</summary>
    private async Task SwitchModeAsync(bool cd)
    {
        if (cd == IsCdMode) return;
        bool busy = cd ? Video.IsBusy : Cd.IsBusy;
        if (busy)
        {
            //both sides use the same drive: finish (or cancel) what's going on first.
            OnPropertyChanged(nameof(IsCdMode));      //puts the switch back where it was
            OnPropertyChanged(nameof(IsVideoMode));
            _dialogs.Show(cd
                    ? "A DVD / Blu-ray is being read or ripped. Let it finish (or cancel it) before switching to Music CD."
                    : "A music CD is being read or ripped. Let it finish (or cancel it) before switching to DVD / Blu-ray.",
                "Still busy");
            return;
        }
        IsCdMode = cd;
        _settings.Current.Mode = cd ? "Cd" : "Video";
        _settings.Save();
        await ApplyModeAsync();
    }

    /// <summary>Tells each side and the drive box which side is showing.</summary>
    private async Task ApplyModeAsync()
    {
        Drive.CdMode = IsCdMode;
        Drive.Compose();
        await Video.SetActiveAsync(!IsCdMode);
        await Cd.SetActiveAsync(IsCdMode);
    }

    /// <summary>Updates the taskbar progress and the tray tooltip.</summary>
    private void UpdateProgress()
    {
        TaskbarProgress state;
        double percent = 0;
        string tray;

        if (Cd.State == RipState.Ripping)
        {
            state = TaskbarProgress.Normal;
            percent = Cd.Progress;
            tray = $"Ripping CD {percent:0}%";
        }
        else if (Cd.State is RipState.Waiting or RipState.Scanning)
        {
            state = TaskbarProgress.Indeterminate;
            tray = "Waiting for a CD";
        }
        else if (Video.State == RipState.Ripping)
        {
            state = TaskbarProgress.Normal;
            percent = Video.Progress;
            tray = $"Ripping {percent:0}%";
        }
        else if (Video.State is RipState.Waiting or RipState.Scanning)
        {
            state = TaskbarProgress.Indeterminate;
            tray = "Waiting for a disc";
        }
        else if (_queue.Current != null)
        {
            state = TaskbarProgress.Normal;
            percent = _queue.CurrentPercent;
            tray = $"Compressing {percent:0}%";
        }
        else
        {
            state = TaskbarProgress.None;
            tray = "DiscRippa";
        }

        //whole percents only, so the taskbar isn't redrawn for every tiny step.
        TaskbarState = state;
        TaskbarValue = Math.Round(Math.Clamp(percent, 0, 100)) / 100.0;
        if (tray != _trayText)
        {
            _trayText = tray;
            _notify.SetStatus(tray);
        }
    }

    /// <summary>After Settings was saved: restarts the app if debug mode was switched, otherwise reloads the presets.</summary>
    private async Task AfterSettingsAsync(bool saved)
    {
        if (!saved) return;
        bool wasDebug = _debugWhenSettingsOpened;
        if (_settings.Current.DebugMode != wasDebug)
        {
            if (!SwitchDebugMode(_settings.Current.DebugMode))
            {
                _settings.Current.DebugMode = wasDebug;   //couldn't switch right now; keep things as they were
                _settings.Save();
            }
            return;
        }
        await _presets.LoadAsync();
    }

    /// <summary>Debug mode swaps the whole engine, so the app restarts to switch. Returns false if it didn't.</summary>
    private bool SwitchDebugMode(bool on)
    {
        if (Video.State == RipState.Ripping || Cd.State == RipState.Ripping || _queue.Current != null)
        {
            _dialogs.Show("A rip or compression is running. Let it finish (or stop it) first, then switch debug mode.", "Debug mode");
            return false;
        }

        var text = on
            ? "The app will restart in debug mode.\n\nRipping and compressing will be simulated so you can try the whole workflow without a disc."
            : "The app will restart in normal mode, ready to rip real discs.";
        if (!_dialogs.ConfirmOkCancel(text, "Debug mode")) return false;

        _settings.Current.DebugMode = on;
        _settings.Save();
        Video.Shutdown();
        Cd.Shutdown();
        _windows.Restart();
        return true;
    }

    /// <summary>Asked when the window is closing. False = stay open.</summary>
    public bool CanClose()
    {
        bool busy = Video.State == RipState.Ripping || Cd.State == RipState.Ripping || _queue.Current != null;
        return !busy || _dialogs.Confirm(
            "A rip or compression is still running. Quit and stop it?\n\nUnfinished files will be removed. " +
            "A file that was compressing goes back in the queue and starts over next time.",
            "Quit?", defaultNo: true);
    }

    /// <summary>Stops everything and removes half-written files. Called once the window has closed.</summary>
    public void Shutdown()
    {
        Video.Shutdown();
        Cd.Shutdown();
        _queue.Shutdown();
    }

    #endregion
}
