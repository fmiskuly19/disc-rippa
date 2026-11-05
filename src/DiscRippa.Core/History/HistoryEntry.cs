using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DiscRippa.Core.History;

/// <summary>One disc that was ripped, and where its files went.</summary>
public sealed class HistoryEntry : ObservableObject
{
    #region Fields

    private string _status = "";
    private bool _isActive;
    private double _progress;
    private int _titleCount;

    #endregion

    #region Properties

    public int Id { get; set; }
    public string When { get; set; } = "";
    public string Disc { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public int Problems { get; set; }

    [JsonIgnore]
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    [JsonIgnore]
    public bool IsActive
    {
        get => _isActive;
        set { if (SetProperty(ref _isActive, value))
        {
            OnPropertyChanged(nameof(FilesText));
            OnPropertyChanged(nameof(Detail));
        } }
    }

    [JsonIgnore]
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    [JsonIgnore]
    public int TitleCount
    {
        get => _titleCount;
        set
        {
            if (SetProperty(ref _titleCount, value)) OnPropertyChanged(nameof(FilesText));
        }
    }

    /// <summary>A music CD (tracks, cyanrip) rather than a DVD / Blu-ray (titles, MakeMKV).</summary>
    [JsonIgnore]
    public bool IsMusic { get; set; }

    [JsonIgnore]
    public string FilesText => IsActive
        ? (TitleCount == 1 ? (IsMusic ? "1 track" : "1 title") : $"{TitleCount} {(IsMusic ? "tracks" : "titles")}")
        : Files.Count == 1 ? Files[0] : IsMusic ? $"{Files.Count} tracks" : $"{Files.Count} files";

    [JsonIgnore]
    public string Detail
    {
        get
        {
            var files = IsActive ? "(still ripping)" : string.Join(", ", Files);
            var text = $"Disc label: {(Disc.Length > 0 ? Disc : "unknown")}\nFolder: {Folder}\nFiles: {files}";
            if (Problems > 0 && IsMusic)
                text += $"\ncyanrip reported {Problems} {(Problems == 1 ? "problem" : "problems")} while reading this CD, so some tracks may have clicks or gaps.";
            else if (Problems > 0)
                text += $"\nMakeMKV reported {Problems} {(Problems == 1 ? "problem" : "problems")} while reading this disc, so the video may have glitches.";
            return text;
        }
    }

    #endregion

    #region Methods

    /// <summary>Shown in the Status column while ripping, e.g. "Ripping 45%".</summary>
    public void SetLiveStatus(string text) => Status = text;

    /// <summary>Checks whether the files are still where they were saved.</summary>
    public void Refresh() => ShowPresence(FilesPresent());

    /// <summary>True if the folder and every file are still there. Can be slow (a sleeping drive), so it's safe off the UI thread.</summary>
    public bool FilesPresent()
    {
        try
        {
            return Directory.Exists(Folder) && Files.All(f => File.Exists(System.IO.Path.Combine(Folder, f)));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Shows whether the files are still there (see <see cref="FilesPresent"/>).</summary>
    public void ShowPresence(bool present)
    {
        if (IsActive) return;   //the live status is owned by the rip in progress
        Status = !present ? "Moved or deleted" : Problems > 0 ? "Ripped (warnings)" : "Ripped";
    }

    /// <summary>The file list changed: refresh what's shown.</summary>
    internal void Touch()
    {
        OnPropertyChanged(nameof(Files));
        OnPropertyChanged(nameof(FilesText));
        OnPropertyChanged(nameof(Detail));
        Refresh();
    }

    /// <summary>Records the finished files and problem count, ending the live progress.</summary>
    internal void Complete(IEnumerable<string> files, int problems)
    {
        Files = files.ToList();
        Problems = problems;
        IsActive = false;
        Progress = 0;
        OnPropertyChanged(nameof(Files));
        OnPropertyChanged(nameof(FilesText));
        OnPropertyChanged(nameof(Detail));
        Refresh();
    }

    #endregion
}
