
namespace DiscRippa.ViewModels.Video;

/// <summary>
/// What was chosen on the disc screen. <see cref="Folder"/> is where the files go: the folder picked for one title, or a
/// new folder named <see cref="Name"/> inside it for several. <see cref="SaveFolder"/> is always the folder picked.
/// </summary>
public sealed record RipChoices(string Name, string Folder, IReadOnlyList<TitleInfo> Titles, bool Compress, string Preset, bool Eject,
                                bool KeepOriginal = true, bool SaveAsMkv = false, string? SaveFolder = null);

/// <summary>One track of a title, ready to show.</summary>
public sealed record StreamRow(string Type, string Codec, string Language, string Details);

/// <summary>One title on the disc, with a tick box for "rip this".</summary>
public sealed class TitleRowViewModel : ObservableObject
{
    #region Fields

    private readonly Action _onSelectionChanged;
    private bool _isSelected;

    #endregion

    #region Properties

    public TitleInfo Info { get; }
    public bool IsMain { get; }
    public IReadOnlyList<StreamRow> Streams { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) _onSelectionChanged();
        }
    }

    public int Number => Info.Id + 1;
    public string Source => Info.SourceFile.Length > 0 ? Info.SourceFile : $"Title {Number}";
    public string Duration => Info.Duration;
    public string Chapters => Info.Chapters > 0 ? Info.Chapters.ToString() : "";
    public string SizeText => Info.Size > 0 ? DiscReviewViewModel.Gb(Info.Size) : Info.SizeText;
    public bool IsShort => Info.Seconds < 120;

    /// <summary>"Main movie · 1920x1080 · 3 audio · 3 subtitles"</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (IsMain) parts.Add("Main movie");
            else if (IsShort) parts.Add("Short clip");
            var video = Info.Streams.FirstOrDefault(s => s.Type == "Video");
            if (video != null && video.VideoSize.Length > 0) parts.Add(video.VideoSize);
            int audio = Info.Streams.Count(s => s.Type == "Audio");
            int subs = Info.Streams.Count(s => s.Type == "Subtitles");
            if (audio > 0) parts.Add($"{audio} audio");
            if (subs > 0) parts.Add(subs == 1 ? "1 subtitle" : $"{subs} subtitles");
            return string.Join("  ·  ", parts);
        }
    }

    #endregion

    #region Constructors

    /// <summary>Creates a row for one title; <paramref name="onSelectionChanged"/> is called when it is ticked.</summary>
    public TitleRowViewModel(TitleInfo info, bool isMain, Action onSelectionChanged)
    {
        Info = info;
        IsMain = isMain;
        _onSelectionChanged = onSelectionChanged;
        Streams = info.Streams.Select(ToRow).ToList();
    }

    #endregion

    #region Methods

    /// <summary>One track as a row for the tracks list.</summary>
    private static StreamRow ToRow(StreamInfo s)
    {
        var details = new List<string>();
        switch (s.Type)
        {
            case "Video":
                if (s.VideoSize.Length > 0) details.Add(s.VideoSize);
                if (s.FrameRate.Length > 0) details.Add(s.FrameRate.Split(' ')[0] + " fps");
                if (s.AspectRatio.Length > 0) details.Add(s.AspectRatio);
                break;
            case "Audio":
                if (s.ChannelLayout.Length > 0) details.Add(s.ChannelLayout);
                else if (s.Channels > 0) details.Add($"{s.Channels} channels");
                break;
        }
        if (s.Description.Length > 0 && details.Count == 0) details.Add(s.Description);
        var codec = s.CodecLong.Length > 0 ? s.CodecLong : s.CodecShort;
        return new StreamRow(s.Type, codec, s.LangName, string.Join("  ·  ", details));
    }

    #endregion
}

/// <summary>
/// The disc screen shown after a disc has been read: everything on the disc, what is going to be ripped
/// and where, and the RIP button. The window binds to this; it has no UI code of its own.
/// </summary>
public sealed partial class DiscReviewViewModel : ObservableObject
{
    #region Fields

    private readonly Func<string, long?> _freeSpace;
    private readonly IDialogService? _dialogs;
    private readonly Func<Task<string?>>? _changeFolder;
    private readonly TaskCompletionSource<RipChoices?> _result = new();
    private string _name;
    private string _folder;
    private bool _compress;
    private string _preset;
    private bool _eject = true;
    private bool _keepOriginal;
    private bool _saveAsMkv;
    private TitleRowViewModel? _selectedTitle;
    private bool _isAutomated;
    private bool _ejectLocked;
    private string _automationText = "";
    private double _automationProgress;

    #endregion

    #region Properties

    public DiscFound Disc { get; }
    public ObservableCollection<TitleRowViewModel> Titles { get; }
    public IReadOnlyList<string> PresetChoices { get; }
    public bool CanCompress { get; }

    /// <summary>"The Movie"</summary>
    public string DiscTitle { get; }

    /// <summary>"Blu-ray disc  ·  label MY_MOVIE  ·  BD-RE HL-DT-ST (D:)  ·  4 titles"</summary>
    public string DiscSummary
    {
        get
        {
            var parts = new List<string>();
            if (Disc.DiscType.Length > 0) parts.Add(Disc.DiscType);
            parts.Add($"label {Disc.DiscLabel}");
            var drive = Disc.DriveName.Trim();
            if (drive.Length > 0 || Disc.Device.Length > 0)
                parts.Add(Disc.Device.Length > 0 && drive.Length > 0 ? $"{drive} ({Disc.Device})" : drive + Disc.Device);
            parts.Add(Disc.Titles.Count == 1 ? "1 title" : $"{Disc.Titles.Count} titles");
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>The title whose tracks are listed below the titles.</summary>
    public TitleRowViewModel? SelectedTitle
    {
        get => _selectedTitle;
        set
        {
            if (SetProperty(ref _selectedTitle, value)) OnPropertyChanged(nameof(StreamsHeader));
        }
    }

    public string StreamsHeader => SelectedTitle == null ? "Tracks" : $"Tracks in title {SelectedTitle.Number} ({SelectedTitle.Duration})";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value)) RaisePlan();
        }
    }

    public string Folder
    {
        get => _folder;
        set
        {
            if (SetProperty(ref _folder, value)) RaisePlan();
        }
    }

    public bool Compress
    {
        get => _compress;
        set { if (SetProperty(ref _compress, value))
        {
            OnPropertyChanged(nameof(CanKeepOriginal));
            RaisePlan();
        } }
    }

    /// <summary>The smaller copy as MKV (every audio track and subtitle kept exactly) instead of MP4 (plays everywhere).</summary>
    public bool SaveAsMkv
    {
        get => _saveAsMkv;
        set { if (SetProperty(ref _saveAsMkv, value))
        {
            OnPropertyChanged(nameof(SaveAsMp4));
            RaisePlan();
        } }
    }

    /// <summary>The other radio button.</summary>
    public bool SaveAsMp4
    {
        get => !_saveAsMkv;
        set
        {
            if (value) SaveAsMkv = false;
        }
    }

    /// <summary>Keep the MKV after compressing. Off (the default): it's deleted once the smaller copy is done.</summary>
    public bool KeepOriginal
    {
        get => _keepOriginal;
        set
        {
            if (SetProperty(ref _keepOriginal, value)) RaisePlan();
        }
    }

    /// <summary>The "keep the MKV" choice only means something when a compressed copy is being made.</summary>
    public bool CanKeepOriginal => Compress && CanCompress && IsEditable;

    public string Preset
    {
        get => _preset;
        set
        {
            if (SetProperty(ref _preset, value)) RaisePlan();
        }
    }

    public bool Eject
    {
        get => _eject;
        set
        {
            if (SetProperty(ref _eject, value)) RaisePlan();
        }
    }

    /// <summary>True while automation mode is running this screen: everything is shown but locked.</summary>
    public bool IsAutomated
    {
        get => _isAutomated;
        private set
        {
            if (!SetProperty(ref _isAutomated, value)) return;
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CanChangeEject));
            OnPropertyChanged(nameof(CanKeepOriginal));
            OnPropertyChanged(nameof(CanRip));
        }
    }

    public bool IsEditable => !IsAutomated;

    /// <summary>The user stopped the countdown to choose this disc's options themselves.</summary>
    public bool TookOver { get; private set; }

    /// <summary>"Automation mode: ripping the main movie in 3 s…"</summary>
    public string AutomationText { get => _automationText; set => SetProperty(ref _automationText, value); }

    /// <summary>0-100 as the countdown runs out.</summary>
    public double AutomationProgress { get => _automationProgress; set => SetProperty(ref _automationProgress, value); }

    /// <summary>Eject can't be switched off when "Keep Ripping!" or automation will need the tray open.</summary>
    public bool CanChangeEject => !_ejectLocked && IsEditable;

    public IReadOnlyList<TitleInfo> SelectedTitles => Titles.Where(r => r.IsSelected).Select(r => r.Info).ToList();
    public long NeededBytes => SelectedTitles.Sum(t => t.Size);

    /// <summary>More than one title is ticked: they go together in a folder of their own, and the name box names that folder.</summary>
    public bool IsMultiple => SelectedTitles.Count > 1;

    public string NameLabel => IsMultiple ? "Folder name" : "File name";

    /// <summary>Where the files will actually go: the folder picked, or (several titles) a new folder inside it.</summary>
    public string TargetFolder
    {
        get
        {
            var folder = Folder.Trim();
            var name = FileNames.Safe(Name);
            if (!IsMultiple || folder.Length == 0 || name.Length == 0) return folder;
            try
            {
                return Path.Combine(folder, name);
            }
            catch
            {
                return folder;
            }
        }
    }

    /// <summary>Under the name box: where several titles will end up.</summary>
    public string NameHint => IsMultiple
        ? $"The {SelectedTitles.Count} files go in a new folder: {TargetFolder}"
        : "";

    /// <summary>The files the rip will create, named the same way the rip names them.</summary>
    public IReadOnlyList<string> PlannedFiles
    {
        get
        {
            var name = FileNames.Safe(Name);
            var titles = SelectedTitles;
            int count = titles.Count;
            var files = new List<string>();
            for (int i = 1; i <= count; i++)
            {
                var file = RipSession.TitleFileName(name, i, count, titles[i - 1]);
                var target = TargetFolder;
                if (target.Length > 0 && Path.IsPathRooted(target))
                {
                    try
                    {
                        file = Path.GetFileName(FileNames.UniquePath(Path.Combine(target, file)));
                    }
                    catch
                    {
                    }
                }
                files.Add(file);
            }
            return files;
        }
    }

    /// <summary>"1 title  ·  1:52:10  ·  about 25.3 GB"</summary>
    public string SelectionText
    {
        get
        {
            var titles = SelectedTitles;
            if (titles.Count == 0) return "No titles selected. Tick at least one title on the left.";
            var seconds = titles.Sum(t => t.Seconds);
            var length = TimeSpan.FromSeconds(seconds);
            var lengthText = length.TotalHours >= 1 ? $"{(int)length.TotalHours}:{length.Minutes:00}:{length.Seconds:00}" : $"{length.Minutes}:{length.Seconds:00}";
            var count = titles.Count == 1 ? "1 title" : $"{titles.Count} titles";
            return NeededBytes > 0 ? $"{count}  ·  {lengthText} of video  ·  about {Gb(NeededBytes)}" : $"{count}  ·  {lengthText} of video";
        }
    }

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

    public string SpaceText
    {
        get
        {
            var folder = Folder.Trim();
            if (folder.Length == 0) return "No output path set.";
            if (!Directory.Exists(folder)) return "This folder will be created when the rip starts.";
            var free = _freeSpace(folder);
            if (free == null) return "";
            var text = $"{Gb(free.Value)} free on that drive";
            if (SpaceIsLow) text += " — not enough room for this rip!";
            return text;
        }
    }

    public bool SpaceIsLow
    {
        get
        {
            var folder = Folder.Trim();
            if (folder.Length == 0 || !Directory.Exists(folder)) return false;
            var free = _freeSpace(folder);
            return free != null && NeededBytes > 0 && free.Value < NeededBytes;
        }
    }

    public string FormatText => "MKV, with the video, audio and subtitle tracks copied exactly as they are on the disc (no quality loss).";

    public string AfterText
    {
        get
        {
            var parts = new List<string>();
            var kind = SaveAsMkv ? "MKV" : "MP4";
            parts.Add(!(Compress && CanCompress) ? "No compression."
                : KeepOriginal ? $"Then make a smaller {kind} copy with HandBrake ({Preset}), saved alongside. The original MKV is kept."
                : $"Then make a smaller {kind} copy with HandBrake ({Preset}). The original MKV is deleted once the copy is done.");
            parts.Add(!Eject ? "The disc stays in the drive."
                : _ejectLocked ? "The disc is ejected when it's done, ready for the next one."
                : "The disc is ejected when it's done.");
            return string.Join("\n", parts);
        }
    }

    public bool CanRip => IsEditable && SelectedTitles.Count > 0 && FileNames.Safe(Name).Length > 0 && Name.Trim().Length > 0 && Folder.Trim().Length > 0;

    /// <summary>Completes when the user clicks RIP (with their choices) or Cancel (null).</summary>
    public Task<RipChoices?> Result => _result.Task;

    #endregion

    #region Constructors

    /// <summary>Creates the disc screen for this disc, with the main movie ticked.</summary>
    public DiscReviewViewModel(DiscFound disc, string startFolder, IReadOnlyList<string> presetChoices, string defaultPreset,
                      bool canCompress, Func<string, long?>? freeSpace = null, bool keepOriginal = false, bool saveAsMkv = false,
                      IDialogService? dialogs = null, Func<Task<string?>>? changeFolder = null)
    {
        _dialogs = dialogs;
        _changeFolder = changeFolder;
        _saveAsMkv = saveAsMkv;
        _keepOriginal = keepOriginal;
        Disc = disc;
        _freeSpace = freeSpace ?? FreeSpaceOn;
        PresetChoices = presetChoices;
        _preset = defaultPreset;
        CanCompress = canCompress;
        _compress = canCompress;   //a smaller copy is made by default (when HandBrakeCLI is there)
        _folder = startFolder;

        var main = disc.Titles.OrderByDescending(t => t.Seconds).ThenByDescending(t => t.Size).First();
        Titles = new ObservableCollection<TitleRowViewModel>(disc.Titles.Select(t => new TitleRowViewModel(t, t == main, OnSelectionChanged)));
        foreach (var row in Titles) if (row.IsMain) row.IsSelected = true;
        _selectedTitle = Titles.First(r => r.IsMain);

        DiscTitle = FileNames.CleanDiscName(disc.DiscName.Length > 0 ? disc.DiscName : disc.DiscLabel);
        _name = DiscTitle;
    }

    #endregion

    #region Commands

    /// <summary>"Let me choose": stop the countdown and unlock the screen for this disc.</summary>
    [RelayCommand]
    public void TakeOver()
    {
        if (!IsAutomated) return;
        TookOver = true;
        IsAutomated = false;
        AutomationText = "";
        AutomationProgress = 0;
    }

    /// <summary>Ticks only the main movie.</summary>
    [RelayCommand]
    public void SelectMainOnly()
    {
        foreach (var r in Titles) r.IsSelected = r.IsMain;
    }

    /// <summary>Every title longer than 2 minutes (e.g. all the episodes on a TV disc).</summary>
    [RelayCommand]
    public void SelectAllLong()
    {
        foreach (var r in Titles) r.IsSelected = !r.IsShort;
        if (!Titles.Any(r => r.IsSelected)) SelectMainOnly();
    }

    /// <summary>Unticks every title.</summary>
    [RelayCommand]
    public void SelectNone()
    {
        foreach (var r in Titles) r.IsSelected = false;
    }

    /// <summary>Closes the disc screen without ripping.</summary>
    [RelayCommand]
    public void Cancel() => _result.TrySetResult(null);

    /// <summary>"Change…" next to the output path: opens Settings at that setting, then uses whatever it is now.</summary>
    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        if (_changeFolder == null) return;
        var folder = await _changeFolder();
        if (!string.IsNullOrWhiteSpace(folder)) Folder = folder;
    }

    /// <summary>The RIP button: the checks a person gets as questions, then the choices go to the rip.</summary>
    [RelayCommand]
    private void Rip()
    {
        var problem = Validate();
        if (problem != null)
        {
            _dialogs?.Show(problem, "Not quite ready");
            return;
        }

        var folder = Folder.Trim();
        if (!Directory.Exists(folder))
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                _dialogs?.Show($"Couldn't create that folder:\n{ex.Message}", "Folder problem", DialogIcon.Warning);
                return;
            }
        }

        if (SpaceIsLow && _dialogs != null && !_dialogs.Confirm(
                $"This rip needs about {Gb(NeededBytes)}, but there isn't that much free space on that drive. " +
                "It will probably fail part-way.\n\nRip anyway?", "Not enough space", DialogIcon.Warning, defaultNo: true))
            return;

        Confirm();
    }

    #endregion

    #region Methods

    /// <summary>Ticks and locks "Eject" (the next disc needs the tray open).</summary>
    public void LockEject()
    {
        _ejectLocked = true;
        Eject = true;
        OnPropertyChanged(nameof(CanChangeEject));
        RaisePlan();
    }

    /// <summary>Automation's choices: the main movie only, compressed with Auto (if HandBrake is there), ejected after.</summary>
    public void StartAutomation()
    {
        SelectMainOnly();
        SelectedTitle = Titles.FirstOrDefault(r => r.IsMain) ?? SelectedTitle;
        Compress = CanCompress;
        Preset = Presets.Auto;
        LockEject();
        IsAutomated = true;
    }

    /// <summary>A reason the rip can't start yet, or null if it can.</summary>
    public string? Validate()
    {
        if (SelectedTitles.Count == 0) return "Tick at least one title to rip.";
        if (FileNames.Safe(Name).Length == 0) return IsMultiple ? "Give the folder a name first." : "Give the file a name first.";
        var folder = Folder.Trim();
        if (folder.Length == 0 || !Path.IsPathRooted(folder)) return "Set an output path first (click Change…).";
        return null;
    }

    /// <summary>Hands the choices to the rip.</summary>
    public void Confirm()
    {
        _result.TrySetResult(new RipChoices(FileNames.Safe(Name), TargetFolder, SelectedTitles, Compress && CanCompress, Preset, Eject,
                                              KeepOriginal || !(Compress && CanCompress), SaveAsMkv, Folder.Trim()));
    }

    /// <summary>A title was ticked or unticked.</summary>
    private void OnSelectionChanged() => RaisePlan();

    /// <summary>Refreshes everything worked out from the choices.</summary>
    private void RaisePlan()
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(FilesText));
        OnPropertyChanged(nameof(PlannedFiles));
        OnPropertyChanged(nameof(SpaceText));
        OnPropertyChanged(nameof(SpaceIsLow));
        OnPropertyChanged(nameof(AfterText));
        OnPropertyChanged(nameof(CanRip));
        OnPropertyChanged(nameof(NeededBytes));
        OnPropertyChanged(nameof(IsMultiple));
        OnPropertyChanged(nameof(NameLabel));
        OnPropertyChanged(nameof(NameHint));
        OnPropertyChanged(nameof(TargetFolder));
    }

    /// <summary>"25.3 GB".</summary>
    public static string Gb(long bytes) => $"{bytes / 1_000_000_000.0:0.0} GB";

    /// <summary>Free space on the drive holding <paramref name="folder"/>, or null if unknown.</summary>
    private static long? FreeSpaceOn(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
