namespace DiscRippa.ViewModels.Shared;

/// <summary>
/// The drive box at the top of the window: one drive name and one description of what's in it, the same in both modes.
/// Windows is asked directly every few seconds (fast, and it sees every kind of disc). While a mode is reading or ripping
/// a disc, that mode's own words ("Ripping: THE_MOVIE") are shown instead, and Windows isn't asked, so nothing gets in
/// the way of the rip.
/// </summary>
public sealed partial class DriveBoxViewModel : ObservableObject
{
    #region Fields

    private readonly RunMode _mode;
    private readonly ICdDriveProbe _probe;
    private readonly IDriveService _drives;
    private readonly VideoRipViewModel _video;
    private readonly CdRipViewModel _cd;
    private Ticker? _ticker;
    private IReadOnlyList<CdDriveState>? _found;
    private bool _checking;

    #endregion

    #region Properties

    [ObservableProperty] private string title = "Looking for your disc drive…";
    [ObservableProperty] private string state = "";

    /// <summary>The drive shown ("G:"), for the Eject button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject))]
    [NotifyCanExecuteChangedFor(nameof(EjectCommand))]
    private string letter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEject))]
    [NotifyCanExecuteChangedFor(nameof(EjectCommand))]
    private bool ejecting;

    /// <summary>True while the Music CD side is showing (only matters in debug mode, where each side has its own pretend drive).</summary>
    public bool CdMode { get; set; }

    /// <summary>Eject works whenever no disc is being read or ripped (pulling the disc mid-rip would wreck the file).</summary>
    public bool CanEject => !Ejecting && !_video.State.IsWorking() && !_cd.State.IsWorking() && (_mode.IsDebug || Letter.Length > 0);

    #endregion

    #region Constructors

    /// <summary>Creates the drive box, which follows both sides of the app.</summary>
    public DriveBoxViewModel(RunMode mode, ICdDriveProbe probe, IDriveService drives, VideoRipViewModel video, CdRipViewModel cd)
    {
        _mode = mode;
        _probe = probe;
        _drives = drives;
        _video = video;
        _cd = cd;
        _video.PropertyChanged += (_, e) =>
        {
            Compose();
            if (e.PropertyName == nameof(VideoRipViewModel.State)) RefreshCanEject();
        };
        _cd.PropertyChanged += (_, e) =>
        {
            Compose();
            if (e.PropertyName == nameof(CdRipViewModel.State)) RefreshCanEject();
        };
        _drives.DriveNamesChanged += Compose;
    }

    #endregion

    #region Commands

    /// <summary>The ⟳ button: look at the drive again right now. Each side then re-checks so its message catches up.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        State = "Checking the drive…";
        await CheckAsync();
        await ActiveSideCatchUpAsync();
    }

    /// <summary>The ⏏ button: open the tray.</summary>
    [RelayCommand(CanExecute = nameof(CanEject))]
    private async Task EjectAsync()
    {
        Ejecting = true;
        State = "Opening the tray…";
        try
        {
            var letter = Letter;
            bool ok = await Task.Run(() => CdMode ? _drives.EjectCdDrive(letter) : _drives.EjectVideoDrive(letter));
            if (!ok) State = "Couldn't open the tray. Use the drive's own button.";
            await Task.Delay(1500);   //give the drive a moment before looking again
        }
        finally
        {
            Ejecting = false;
        }
        await CheckAsync();
        await ActiveSideCatchUpAsync();
    }

    #endregion

    #region Methods

    /// <summary>Re-checks whether the Eject button can be used.</summary>
    private void RefreshCanEject()
    {
        OnPropertyChanged(nameof(CanEject));
        EjectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Starts looking at the drive every few seconds.</summary>
    public async Task StartAsync()
    {
        _ticker ??= Ticker.Start(TimeSpan.FromSeconds(3), CheckAsync);
        await CheckAsync();
    }

    /// <summary>Lets the side that's showing look at the drive again, so its message matches.</summary>
    private Task ActiveSideCatchUpAsync() => CdMode ? _cd.SetActiveAsync(true) : _video.SetActiveAsync(true);

    /// <summary>Asks Windows what is in the drives (not while a disc is being read or ripped).</summary>
    private async Task CheckAsync()
    {
        if (_mode.IsDebug || _checking || _video.State.IsWorking() || _cd.State.IsWorking())
        {
            Compose();
            return;
        }
        _checking = true;
        try
        {
            _found = await Task.Run(() => _probe.GetDrives());
        }
        catch
        {
        }
        finally
        {
            _checking = false;
        }
        Compose();
    }

    /// <summary>Works out the drive name and the line under it.</summary>
    public void Compose()
    {
        if (Ejecting) return;

        //debug mode: each side has its own pretend drive, so show the side that's in front.
        if (_mode.IsDebug)
        {
            Title = CdMode ? _cd.DriveTitle : _video.DriveTitle;
            State = CdMode ? _cd.DriveState : _video.DriveState;
            return;
        }

        if (_found != null)
        {
            var drive = _found.FirstOrDefault(d => d.IsReady) ?? _found.FirstOrDefault();
            Letter = drive?.Letter ?? "";
            Title = drive == null ? "No disc drive found"
                : _drives.DriveName(drive.Letter, $"Disc drive ({drive.Letter})") + (_found.Count > 1 ? $"   (+{_found.Count - 1} more)" : "");
            State = drive == null ? "Connect a CD, DVD or Blu-ray drive."
                : !drive.IsReady ? "No disc in the drive"
                : drive.IsAudioCd ? $"Music CD inserted ({Text.Plural(drive.Tracks.Count, "track")}, {CdDriveState.FormatLength(drive.TotalSeconds)})"
                : drive.Label.Length > 0 ? $"Disc inserted: {FileNames.CleanDiscName(drive.Label)}"
                : "Disc inserted";
        }

        //while a disc is being read or ripped, the mode doing it says what's happening.
        if (_video.State.IsWorking()) State = _video.DriveState;
        else if (_cd.State.IsWorking()) State = _cd.DriveState;
    }

    #endregion
}
