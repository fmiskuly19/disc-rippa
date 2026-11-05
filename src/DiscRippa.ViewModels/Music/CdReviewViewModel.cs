namespace DiscRippa.ViewModels.Music;

/// <summary>What was chosen on the CD screen. <see cref="MusicRoot"/> is the music folder; <see cref="Folder"/> the album's.</summary>
public sealed record CdRipChoices(string Artist, string Album, string Year, string MusicRoot, string Folder, IReadOnlyList<int> Tracks,
                                  CdMetadata Metadata, bool Eject);

/// <summary>One track on the CD screen, with a tick box and an editable title.</summary>
public sealed partial class CdTrackRowViewModel : ObservableObject
{
    #region Fields

    private readonly Action _changed;

    #endregion

    #region Properties

    public CdTrack Track { get; }
    public int Number => Track.Number;
    public string NumberText => Track.Number.ToString("00");
    public string Duration => Track.Duration;
    [ObservableProperty] private bool isSelected = true;
    [ObservableProperty] private string title;

    #endregion

    #region Constructors

    /// <summary>Creates a row for one track; <paramref name="changed"/> is called when it is ticked or renamed.</summary>
    public CdTrackRowViewModel(CdTrack track, string title, Action changed)
    {
        Track = track;
        this.title = title;
        _changed = changed;
    }

    #endregion

    #region Methods

    /// <summary>Called when is selected changes.</summary>
    partial void OnIsSelectedChanged(bool value) => _changed();

    /// <summary>Called when title changes.</summary>
    partial void OnTitleChanged(string value) => _changed();

    #endregion
}

/// <summary>The music CD screen: album details, track list, where the files go, and RIP.</summary>
public sealed partial class CdReviewViewModel : ObservableObject
{
    #region Fields

    private readonly IDialogService? _dialogs;
    private readonly TaskCompletionSource<CdRipChoices?> _result = new();
    private bool _ejectLocked;

    #endregion

    #region Properties

    public CdDriveState Cd { get; }
    public CdMetadata Metadata { get; }
    public ObservableCollection<CdTrackRowViewModel> Tracks { get; }

    /// <summary>Names were found, on the disc (CD-Text) or online.</summary>
    public bool FoundNames => Metadata.Found;

    public string LookupText => Metadata.Source switch
    {
        MetadataSource.CdText => "Names read from the CD itself (CD-Text), so nothing was looked up online. You can change any of them.",
        MetadataSource.MusicBrainz => "This CD has no names stored on it, so they were looked up on MusicBrainz. You can change any of them.",
        _ => "This CD has no names stored on it and wasn't found online. Type in the artist, album and track names (or rip with plain track numbers).",
    };

    /// <summary>"Music CD  ·  12 tracks  ·  48:21  ·  drive G:"</summary>
    public string DiscSummary => $"Music CD  ·  {Text.Plural(Cd.Tracks.Count, "track")}  ·  {CdDriveState.FormatLength(Cd.TotalSeconds)}  ·  drive {Cd.Letter}";

    [ObservableProperty] private string artist;
    [ObservableProperty] private string album;
    [ObservableProperty] private string year;
    [ObservableProperty] private string musicRoot;
    [ObservableProperty] private bool eject = true;
    public string AlbumFolder => Cyanrip.AlbumFolder(MusicRoot.Trim(), Artist, Album, Metadata.Disc, Metadata.TotalDiscs);

    /// <summary>"Disc 1 of 3: Volume 1, 1971-1975", or "" for a single CD.</summary>
    public string DiscOfText => Metadata.TotalDiscs <= 1 ? ""
        : $"Disc {Metadata.Disc} of {Metadata.TotalDiscs}" + (Metadata.DiscName.Length > 0 ? $": {Metadata.DiscName}" : "");

    public IReadOnlyList<CdTrackRowViewModel> SelectedTracks => Tracks.Where(t => t.IsSelected).ToList();

    public string SelectionText
    {
        get
        {
            var sel = SelectedTracks;
            if (sel.Count == 0) return "No tracks selected. Tick at least one track.";
            var secs = sel.Sum(t => t.Track.Seconds);
            var mb = secs / 60.0 * 10.1 * 0.6;   //FLAC is roughly 60% of the CD's raw 10.1 MB a minute
            return $"{Text.Plural(sel.Count, "track")}  ·  {CdDriveState.FormatLength(secs)}  ·  about {mb:0} MB of FLAC";
        }
    }

    public IReadOnlyList<string> PlannedFiles
        => SelectedTracks.Select(t => $"{t.NumberText} - {FileNames.Safe(t.Title)}.flac").ToList();

    public string FilesText
    {
        get
        {
            var files = PlannedFiles;
            if (files.Count == 0) return "—";
            if (files.Count <= 4) return string.Join("\n", files);
            return string.Join("\n", files.Take(3)) + $"\n… and {files.Count - 3} more";
        }
    }

    public string FormatText => "FLAC: lossless, an exact copy of the CD audio, tagged with the album details and cover art. Each track is checked against AccurateRip.";

    public string AfterText => !Eject ? "The CD stays in the drive."
        : _ejectLocked ? "The CD is ejected when it's done, ready for the next one."
        : "The CD is ejected when it's done.";

    public bool CanRip => IsEditable && SelectedTracks.Count > 0 && MusicRoot.Trim().Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(CanChangeEject), nameof(CanRip))]
    private bool isAutomated;

    public bool IsEditable => !IsAutomated;
    public bool TookOver { get; private set; }
    [ObservableProperty] private string automationText = "";
    [ObservableProperty] private double automationProgress;
    public bool CanChangeEject => !_ejectLocked && IsEditable;

    /// <summary>Completes when the user clicks RIP (with their choices) or Cancel (null).</summary>
    public Task<CdRipChoices?> Result => _result.Task;

    #endregion

    #region Constructors

    /// <summary>Creates the CD screen for this CD and the names found for it.</summary>
    public CdReviewViewModel(CdDriveState cd, CdMetadata meta, string musicRoot, IDialogService? dialogs = null)
    {
        _dialogs = dialogs;
        Cd = cd;
        Metadata = meta;
        this.musicRoot = musicRoot;
        artist = meta.AlbumArtist;
        album = meta.Album.Length > 0 ? meta.Album : FileNames.CleanDiscName(cd.Label);
        year = meta.Date.Length >= 4 ? meta.Date[..4] : meta.Date;
        Tracks = new ObservableCollection<CdTrackRowViewModel>(cd.Tracks.Select(t =>
            new CdTrackRowViewModel(t, meta.TrackTitles.TryGetValue(t.Number, out var name) && name.Length > 0 ? name : $"Track {t.Number:00}",
                                    RaisePlan)));
    }

    #endregion

    #region Commands

    /// <summary>"Let me choose": stops the automation countdown and makes the screen editable.</summary>
    [RelayCommand]
    public void TakeOver()
    {
        if (!IsAutomated) return;
        TookOver = true;
        IsAutomated = false;
        AutomationText = "";
        AutomationProgress = 0;
    }

    /// <summary>Ticks every track.</summary>
    [RelayCommand]
    public void SelectAll()
    {
        foreach (var t in Tracks) t.IsSelected = true;
    }

    /// <summary>Unticks every track.</summary>
    [RelayCommand]
    public void SelectNone()
    {
        foreach (var t in Tracks) t.IsSelected = false;
    }

    /// <summary>Closes the CD screen without ripping.</summary>
    [RelayCommand]
    public void Cancel() => _result.TrySetResult(null);

    /// <summary>Lets the user pick the music folder.</summary>
    [RelayCommand]
    private void BrowseMusicFolder()
    {
        var picked = _dialogs?.PickFolder("Your music folder (Artist\\Album folders are made inside it)",
                                          Directory.Exists(MusicRoot) ? MusicRoot : null);
        if (picked != null) MusicRoot = picked;
    }

    /// <summary>The RIP button: check the choices, make the music folder, then hand the choices to the rip.</summary>
    [RelayCommand]
    private void Rip()
    {
        var problem = Validate();
        if (problem != null)
        {
            _dialogs?.Show(problem, "Not quite ready");
            return;
        }
        try
        {
            Directory.CreateDirectory(MusicRoot.Trim());
        }
        catch (Exception ex)
        {
            _dialogs?.Show($"Couldn't create that folder:\n{ex.Message}", "Folder problem", DialogIcon.Warning);
            return;
        }
        Confirm();
    }

    #endregion

    #region Methods

    /// <summary>Called when artist changes.</summary>
    partial void OnArtistChanged(string value) => RaisePlan();

    /// <summary>Called when album changes.</summary>
    partial void OnAlbumChanged(string value) => RaisePlan();

    /// <summary>Called when year changes.</summary>
    partial void OnYearChanged(string value) => RaisePlan();

    /// <summary>Called when music root changes.</summary>
    partial void OnMusicRootChanged(string value) => RaisePlan();

    /// <summary>Called when eject changes.</summary>
    partial void OnEjectChanged(bool value) => RaisePlan();

    /// <summary>Ticks and locks "Eject" (the next CD needs the tray open).</summary>
    public void LockEject()
    {
        _ejectLocked = true;
        Eject = true;
        OnPropertyChanged(nameof(CanChangeEject));
        RaisePlan();
    }

    /// <summary>Automation's choices: every track, names as found, ejected after.</summary>
    public void StartAutomation()
    {
        SelectAll();
        LockEject();
        IsAutomated = true;
    }

    /// <summary>A reason the rip can't start yet, or null if it can.</summary>
    public string? Validate()
    {
        if (SelectedTracks.Count == 0) return "Tick at least one track to rip.";
        var root = MusicRoot.Trim();
        if (root.Length == 0 || !Path.IsPathRooted(root)) return "Pick a music folder (use Browse…).";
        return null;
    }

    /// <summary>Hands the choices to the rip.</summary>
    public void Confirm()
    {
        var meta = new CdMetadata
        {
            Album = Album.Trim(), AlbumArtist = Artist.Trim(), Date = Year.Trim(),
            Disc = Metadata.Disc, TotalDiscs = Metadata.TotalDiscs, DiscName = Metadata.DiscName, DriveModel = Metadata.DriveModel,
            Source = Metadata.Source,
        };
        foreach (var t in Tracks)
        {
            meta.TrackTitles[t.Number] = t.Title.Trim();
            if (Metadata.TrackArtists.TryGetValue(t.Number, out var a)) meta.TrackArtists[t.Number] = a;
        }
        _result.TrySetResult(new CdRipChoices(Artist.Trim(), Album.Trim(), Year.Trim(), MusicRoot.Trim(), AlbumFolder,
                                              SelectedTracks.Select(t => t.Number).ToList(), meta, Eject));
    }

    /// <summary>Refreshes everything worked out from the choices.</summary>
    private void RaisePlan()
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(PlannedFiles));
        OnPropertyChanged(nameof(FilesText));
        OnPropertyChanged(nameof(AlbumFolder));
        OnPropertyChanged(nameof(AfterText));
        OnPropertyChanged(nameof(CanRip));
    }

    #endregion
}
