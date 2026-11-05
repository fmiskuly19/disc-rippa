using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DiscRippa.Core.History;

public sealed class HistoryStore
{
    #region Fields

    private readonly string? _file;

    #endregion

    #region Properties

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    /// <summary>The music CD history (tracks) rather than the DVD / Blu-ray one (titles).</summary>
    public bool IsMusic { get; }

    #endregion

    #region Constructors

    /// <summary>Keeps the list in <paramref name="file"/>, or only in memory when it is null (debug mode).</summary>
    public HistoryStore(string? file, bool music = false)
    {
        _file = file;
        IsMusic = music;
        if (file == null) return;
        var saved = JsonFile.Load<List<HistoryEntry>>(file);
        if (saved == null) return;
        foreach (var e in saved.OrderByDescending(x => x.Id))
        {
            e.IsMusic = music;
            e.Refresh();
            Entries.Add(e);
        }
    }

    #endregion

    #region Methods

    /// <summary>The id for the next entry.</summary>
    private int NextId() => Entries.Count == 0 ? 1 : Entries.Max(x => x.Id) + 1;

    /// <summary>Adds a finished rip to the top of the list.</summary>
    public HistoryEntry Add(string disc, string name, string folder, IEnumerable<string> files, int problems)
    {
        var entry = new HistoryEntry
        {
            Id = NextId(),
            When = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Disc = disc,
            Name = name,
            Folder = folder,
            Files = files.ToList(),
            Problems = problems,
            IsMusic = IsMusic,
        };
        entry.Refresh();
        Entries.Insert(0, entry);
        Save();
        return entry;
    }

    /// <summary>Puts a disc at the top of the list as soon as it starts ripping. Not saved until it ends.</summary>
    public HistoryEntry BeginRip(string disc, string name, string folder, int titleCount)
    {
        var entry = new HistoryEntry
        {
            Id = NextId(),
            When = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Disc = disc,
            Name = name,
            Folder = folder,
            TitleCount = titleCount,
            IsMusic = IsMusic,
            IsActive = true,
        };
        entry.SetLiveStatus("Starting…");
        Entries.Insert(0, entry);
        return entry;
    }

    /// <summary>Updates the progress shown for a rip that is still running.</summary>
    public void UpdateRip(HistoryEntry entry, double percent, string status, int problems)
    {
        if (!entry.IsActive) return;
        entry.Progress = percent;
        entry.Problems = problems;
        entry.SetLiveStatus(status);
    }

    /// <summary>The rip finished: the entry becomes a normal, saved record.</summary>
    public void FinishRip(HistoryEntry entry, IEnumerable<string> files, int problems)
    {
        entry.Complete(files, problems);
        Save();
    }

    /// <summary>
    /// The rip was aborted or failed. Titles that did finish are kept as a record; if nothing finished,
    /// the row disappears.
    /// </summary>
    public void EndUnfinishedRip(HistoryEntry entry, IReadOnlyCollection<string> finishedFiles, int problems)
    {
        if (finishedFiles.Count > 0)
        {
            FinishRip(entry, finishedFiles, problems);
            return;
        }
        Entries.Remove(entry);
        Save();
    }

    /// <summary>
    /// A file of a ripped disc was replaced (the MKV was deleted after compressing): the entry lists the new file instead,
    /// so it doesn't show as "Moved or deleted".
    /// </summary>
    public void ReplaceFile(string oldPath, string newPath)
    {
        bool changed = false;
        foreach (var e in Entries.Where(x => !x.IsActive))
        {
            for (int i = 0; i < e.Files.Count; i++)
            {
                string full;
                try
                {
                    full = System.IO.Path.GetFullPath(System.IO.Path.Combine(e.Folder, e.Files[i]));
                }
                catch
                {
                    continue;
                }
                if (!string.Equals(full, System.IO.Path.GetFullPath(oldPath), StringComparison.OrdinalIgnoreCase)) continue;
                var sameFolder = string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(newPath)),
                                               System.IO.Path.GetFullPath(e.Folder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
                e.Files[i] = sameFolder ? System.IO.Path.GetFileName(newPath) : newPath;
                e.Touch();
                changed = true;
            }
        }
        if (changed) Save();
    }

    /// <summary>Removes a finished entry from the list. A disc that is still ripping can't be removed.</summary>
    public bool Remove(HistoryEntry entry)
    {
        if (entry.IsActive) return false;
        Entries.Remove(entry);
        Save();
        return true;
    }

    /// <summary>Clears every finished entry (a disc that is still ripping stays).</summary>
    public void Clear()
    {
        foreach (var e in Entries.Where(x => !x.IsActive).ToList()) Entries.Remove(e);
        Save();
    }

    /// <summary>Re-checks every entry's files (they may have been moved or deleted).</summary>
    public void RefreshAll()
    {
        foreach (var e in Entries) e.Refresh();
    }

    /// <summary>
    /// Re-checks every entry's files without holding up the screen: the checks run in the background (a sleeping
    /// external drive can take seconds to answer) and the results are shown when they're in.
    /// </summary>
    public async Task RefreshAllAsync()
    {
        var entries = Entries.ToList();
        var present = await Task.Run(() => entries.Select(e => e.FilesPresent()).ToList()).ConfigureAwait(true);
        for (int i = 0; i < entries.Count; i++) entries[i].ShowPresence(present[i]);
    }

    /// <summary>Writes the list to disk (unless it lives in memory only).</summary>
    private void Save()
    {
        if (_file != null) JsonFile.Save(_file, Entries.Where(e => !e.IsActive).ToList());
    }

    #endregion
}
