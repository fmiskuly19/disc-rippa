namespace DiscRippa.Tests;

public class HistoryStoreTests
{
    #region Methods

    /// <summary>Remembers discs newest first and where the files went.</summary>
    [Fact]
    public void Remembers_discs_newest_first_and_where_the_files_went()
    {
        using var dir = new TempDir();
        dir.File("Movie A.mkv");
        dir.File("Show B - 01.mkv");
        dir.File("Show B - 02.mkv");
        var file = dir.Combine("history.json");

        var store = new HistoryStore(file);
        store.Add("MOVIE_A", "Movie A", dir.Path, new[] { "Movie A.mkv" }, 0);
        store.Add("SHOW_B", "Show B", dir.Path, new[] { "Show B - 01.mkv", "Show B - 02.mkv" }, 2);

        Assert.Equal(new[] { "Show B", "Movie A" }, store.Entries.Select(e => e.Name));
        Assert.Equal("Ripped (warnings)", store.Entries[0].Status);
        Assert.Equal("Ripped", store.Entries[1].Status);
        Assert.Equal("2 files", store.Entries[0].FilesText);
        Assert.Equal("Movie A.mkv", store.Entries[1].FilesText);

        var reloaded = new HistoryStore(file);
        Assert.Equal(new[] { "Show B", "Movie A" }, reloaded.Entries.Select(e => e.Name));
        Assert.Equal(dir.Path, reloaded.Entries[0].Folder);
        Assert.Contains("Disc label: SHOW_B", reloaded.Entries[0].Detail);
        Assert.Contains("2 problems", reloaded.Entries[0].Detail);
    }

    /// <summary>Notices when files have been moved or deleted.</summary>
    [Fact]
    public void Notices_when_files_have_been_moved_or_deleted()
    {
        using var dir = new TempDir();
        var movie = dir.File("Movie.mkv");
        var store = new HistoryStore(dir.Combine("history.json"));
        var entry = store.Add("M", "Movie", dir.Path, new[] { "Movie.mkv" }, 0);
        Assert.Equal("Ripped", entry.Status);

        File.Delete(movie);
        store.RefreshAll();
        Assert.Equal("Moved or deleted", entry.Status);
    }

    /// <summary>Removing and clearing only edits the list.</summary>
    [Fact]
    public void Removing_and_clearing_only_edits_the_list()
    {
        using var dir = new TempDir();
        var movie = dir.File("Movie.mkv");
        var file = dir.Combine("history.json");
        var store = new HistoryStore(file);
        var a = store.Add("A", "A", dir.Path, new[] { "Movie.mkv" }, 0);
        store.Add("B", "B", dir.Path, new[] { "Movie.mkv" }, 0);

        store.Remove(a);
        Assert.Single(new HistoryStore(file).Entries);
        store.Clear();
        Assert.Empty(new HistoryStore(file).Entries);
        Assert.True(File.Exists(movie));   //the ripped file itself is never touched
    }

    /// <summary>A corrupt history file just starts a fresh list.</summary>
    [Fact]
    public void A_corrupt_history_file_just_starts_a_fresh_list()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("history.json"), "{ not json");
        Assert.Empty(new HistoryStore(dir.Combine("history.json")).Entries);
    }

    #endregion
}

public class SettingsTests
{
    #region Methods

    /// <summary>Has sensible defaults.</summary>
    [Fact]
    public void Has_sensible_defaults()
    {
        using var dir = new TempDir();
        var s = AppSettings.Load(dir.Combine("missing.json"));
        Assert.True(s.AutoNext);
        Assert.True(s.Notifications);
        Assert.Equal(Presets.Auto, s.DefaultPreset);
        Assert.Null(s.HandBrakePath);
    }

    /// <summary>Saves and loads.</summary>
    [Fact]
    public void Saves_and_loads()
    {
        using var dir = new TempDir();
        var file = dir.Combine("settings.json");
        new AppSettings { HandBrakePath = @"E:\Downloads\HandBrakeCLI.exe", AutoNext = false, LastFolder = @"E:\Movies", DefaultPreset = "Fast 1080p30" }.Save(file);

        var s = AppSettings.Load(file);
        Assert.Equal(@"E:\Downloads\HandBrakeCLI.exe", s.HandBrakePath);
        Assert.False(s.AutoNext);
        Assert.Equal(@"E:\Movies", s.LastFolder);
        Assert.Equal("Fast 1080p30", s.DefaultPreset);
    }

    /// <summary>Saved tool paths win over searching.</summary>
    [Fact]
    public void Saved_tool_paths_win_over_searching()
    {
        using var dir = new TempDir();
        var fake = dir.File("HandBrakeCLI.exe");
        Assert.Equal(fake, ToolLocator.FindHandBrake(fake));
        Assert.Equal(fake, ToolLocator.FindMakeMkv(fake));
    }

    #endregion
}

public class HistoryReplaceTests
{
    #region Methods

    /// <summary>A deleted MKV is replaced by its compressed copy in the history.</summary>
    [Fact]
    public void A_deleted_MKV_is_replaced_by_its_compressed_copy_in_the_history()
    {
        using var dir = new TempDir();
        dir.File("Movie.mp4", "small");
        var store = new HistoryStore(dir.Combine("history.json"));
        var entry = store.Add("MOVIE", "Movie", dir.Path, new[] { "Movie.mkv" }, 0);
        Assert.Equal("Moved or deleted", entry.Status);

        store.ReplaceFile(dir.Combine("Movie.mkv"), dir.Combine("Movie.mp4"));

        Assert.Equal(new[] { "Movie.mp4" }, entry.Files);
        Assert.Equal("Ripped", entry.Status);
        Assert.Equal(new[] { "Movie.mp4" }, new HistoryStore(dir.Combine("history.json")).Entries.Single().Files);
    }

    #endregion
}
