namespace DiscRippa.ViewModels.Shared;

/// <summary>The Compress Queue tab: files waiting for HandBrake, the one compressing now, and the buttons to manage them.</summary>
public sealed partial class QueueViewModel : ObservableObject
{
    #region Fields

    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".ts", ".m2ts", ".wmv", ".mpg", ".mpeg", ".vob", ".webm",
    };

    private const string VideoFilter = "Video files|*.mkv;*.mp4;*.m4v;*.mov;*.avi;*.ts;*.m2ts;*.wmv;*.mpg;*.mpeg;*.vob;*.webm|All files|*.*";
    private readonly IToolPaths _tools;
    private readonly ISettingsService _settings;
    private readonly PresetService _presets;
    private readonly HistoryStore _videoHistory;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notify;
    private readonly ToolPicker _picker;

    #endregion

    #region Properties

    public CompressQueue Queue { get; }
    public ObservableCollection<CompressJob> Jobs => Queue.Jobs;
    public ObservableCollection<string> PresetChoices => _presets.Choices;

    /// <summary>The quality picked for files added on this tab.</summary>
    [ObservableProperty] private string preset;

    /// <summary>The "Save as" picker: MKV (true) or MP4.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatIndex))]
    private bool saveAsMkv;

    /// <summary>0 = MP4, 1 = MKV, for the picker.</summary>
    public int FormatIndex
    {
        get => SaveAsMkv ? 1 : 0;
        set => SaveAsMkv = value == 1;
    }

    /// <summary>The rows ticked in the list (set by the view; a DataGrid's selection can't be bound directly).</summary>
    public IReadOnlyList<CompressJob> SelectedJobs { get; set; } = Array.Empty<CompressJob>();

    public bool HandBrakeFound => _tools.HandBrake != null;

    /// <summary>"Compress Queue (3)  ·  1 failed"</summary>
    public string Header
    {
        get
        {
            var header = "Compress Queue";
            if (Queue.PendingCount > 0) header += $" ({Queue.PendingCount})";
            if (Queue.FailedCount > 0) header += $"  ·  {Queue.FailedCount} failed";
            return header;
        }
    }

    public string PauseText => Queue.Paused ? "Resume queue" : "Pause queue";

    #endregion

    #region Constructors

    /// <summary>Creates the Compress Queue tab.</summary>
    public QueueViewModel(CompressQueue queue, IToolPaths tools, ISettingsService settings, PresetService presets,
                          HistoryStores history, IDialogService dialogs, INotificationService notify, ToolPicker picker)
    {
        Queue = queue;
        _tools = tools;
        _settings = settings;
        _presets = presets;
        _videoHistory = history.Videos;
        _dialogs = dialogs;
        _notify = notify;
        _picker = picker;

        preset = presets.DefaultPreset;
        saveAsMkv = settings.Current.CompressToMkv;

        queue.Changed += () => OnPropertyChanged(nameof(Header));
        queue.JobFinished += OnJobFinished;
        queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CompressQueue.Paused)) OnPropertyChanged(nameof(PauseText));
        };
        presets.Loaded += () =>
        {
            if (!PresetChoices.Contains(Preset)) Preset = Presets.Auto;
        };
        tools.Changed += () => OnPropertyChanged(nameof(HandBrakeFound));
    }

    #endregion

    #region Commands

    /// <summary>Lets the user pick video files to compress.</summary>
    [RelayCommand]
    private void AddFiles()
    {
        var start = AppFolders.SavedMovieFolder(_settings.Current);
        var files = _dialogs.PickFiles("Pick files to compress", VideoFilter, Directory.Exists(start) ? start : null, multiple: true);
        if (files != null) Queue.Add(files, AppFolders.SavedCompressedFolder(_settings.Current), Preset, saveAsMkv: SaveAsMkv);
    }

    /// <summary>Stops the file being compressed now, after asking.</summary>
    [RelayCommand]
    private void StopCurrent()
    {
        var job = Queue.Current;
        if (job == null)
        {
            _dialogs.Show("Nothing is being compressed right now.", "Stop current");
            return;
        }
        if (_dialogs.Confirm($"Stop compressing {job.FileName}?\n\nThe unfinished file will be removed. Your original is untouched, and you can use Retry later.",
                             "Stop compressing?"))
            Queue.StopCurrent();
    }

    /// <summary>Pauses or resumes the queue.</summary>
    [RelayCommand]
    private void TogglePause() => Queue.SetPaused(!Queue.Paused);

    /// <summary>Cancels the selected jobs.</summary>
    [RelayCommand]
    private void CancelSelected()
    {
        foreach (var job in SelectedJobs.ToList()) Queue.Cancel(job);
    }

    /// <summary>Puts the selected failed or cancelled jobs back in the queue.</summary>
    [RelayCommand]
    private void RetrySelected()
    {
        foreach (var job in SelectedJobs.ToList()) Queue.Retry(job);
    }

    /// <summary>Removes the selected jobs from the list.</summary>
    [RelayCommand]
    private void RemoveSelected()
    {
        bool blocked = false;
        foreach (var job in SelectedJobs.ToList())
            if (!Queue.Remove(job)) blocked = true;
        if (blocked)
            _dialogs.Show("That file is being compressed right now. Stop it first, then remove it.", "Still running");
    }

    /// <summary>Removes finished, failed and cancelled jobs from the list.</summary>
    [RelayCommand]
    private void ClearFinished() => Queue.ClearFinished();

    /// <summary>Lets the user pick HandBrakeCLI, then reloads the presets.</summary>
    [RelayCommand]
    private async Task LocateHandBrakeAsync()
    {
        if (!_picker.LocateHandBrake()) return;
        await _presets.LoadAsync();
        Queue.Pump();
    }

    #endregion

    #region Methods

    /// <summary>Files dropped on the tab. Anything that isn't a video file is left out.</summary>
    public void AddDropped(IEnumerable<string> dropped)
    {
        if (!HandBrakeFound) return;
        var files = dropped.Where(f => File.Exists(f) && VideoExtensions.Contains(Path.GetExtension(f))).ToList();
        if (files.Count == 0)
        {
            _dialogs.Show("None of those look like video files.", "Add files");
            return;
        }
        Queue.Add(files, AppFolders.SavedCompressedFolder(_settings.Current), Preset, saveAsMkv: SaveAsMkv);
    }

    /// <summary>Updates the history and sends a notification when a job ends.</summary>
    private void OnJobFinished(CompressJob job)
    {
        if (job.Status == JobStatus.Done && job.OriginalDeleted)
            _videoHistory.ReplaceFile(job.Path, job.Output);   //the Ripped Discs tab now points at the compressed copy

        if (job.Status == JobStatus.Done)
        {
            var text = Path.GetFileName(job.Output);
            if (Queue.PendingCount == 0) text += "\nThe compress queue is now empty.";
            _notify.Notify("Compression finished", text);
        }
        else if (job.Status == JobStatus.Failed)
        {
            _notify.Notify("Compression failed", job.FileName + "\n" + Text.FirstLine(job.Error));
        }
    }

    #endregion
}
