namespace DiscRippa.ViewModels.Shared;

/// <summary>The "Ripped Discs" and "Ripped CDs" tabs: what was ripped, and where it went.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    #region Fields

    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly string _thing;   //"disc" or "CD"

    #endregion

    #region Properties

    public HistoryStore Store { get; }

    public string IntroText => Store.IsMusic
        ? "Every CD you've ripped, and where the FLAC files ended up. Double-click a row to open its folder."
        : "Every disc you've ripped, and where the files ended up. Double-click a row to open its folder.";

    /// <summary>Column headings: "Album" / "Tracks" for CDs, "Name" / "Files" for discs.</summary>
    public string NameHeader => Store.IsMusic ? "Album" : "Name";

    public string FilesHeader => Store.IsMusic ? "Tracks" : "Files";
    public ObservableCollection<HistoryEntry> Entries => Store.Entries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDetail))]
    private HistoryEntry? selected;

    /// <summary>The files and folder of the selected disc, shown under the list.</summary>
    public string SelectedDetail => Selected?.Detail ?? "";

    #endregion

    #region Constructors

    /// <summary>Creates the tab for this history (discs or CDs).</summary>
    public HistoryViewModel(HistoryStore store, IDialogService dialogs, IShellService shell)
    {
        Store = store;
        _dialogs = dialogs;
        _shell = shell;
        _thing = store.IsMusic ? "CD" : "disc";
    }

    #endregion

    #region Commands

    /// <summary>Opens the selected entry's folder in Explorer.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected is not HistoryEntry entry)
        {
            _dialogs.Show($"Click a {_thing} in the list first.", "Open folder");
            return;
        }
        if (!Directory.Exists(entry.Folder))
        {
            _dialogs.Show($"This folder no longer exists:\n{entry.Folder}", "Folder not found", DialogIcon.Warning);
            return;
        }
        _shell.OpenFolder(entry.Folder, entry.Files.FirstOrDefault());
    }

    /// <summary>Removes the selected entry from the list (the files stay).</summary>
    [RelayCommand]
    private void Remove()
    {
        if (Selected is HistoryEntry entry && !Store.Remove(entry))
            _dialogs.Show($"That {_thing} is still being ripped. It can be removed once it has finished.", "Still ripping");
    }

    /// <summary>Empties the list after asking (the files stay).</summary>
    [RelayCommand]
    private void Clear()
    {
        if (Entries.Count == 0) return;
        if (_dialogs.Confirm($"Clear the list of ripped {_thing}s?\n\nYour files are not touched.", "Clear list"))
            Store.Clear();
    }

    #endregion

    #region Methods

    /// <summary>Called when the tab is opened: files may have been moved or deleted since.</summary>
    public async void Refresh()
    {
        try
        {
            await Store.RefreshAllAsync();
            OnPropertyChanged(nameof(SelectedDetail));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    #endregion
}
