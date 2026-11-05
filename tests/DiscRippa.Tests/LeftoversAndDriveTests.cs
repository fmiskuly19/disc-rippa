namespace DiscRippa.Tests;

public class LeftoverFileTests
{
    #region Methods

    /// <summary>A leftover file with MakeMKVs name is still recognised as this rips output.</summary>
    [Fact]
    public async Task A_leftover_file_with_MakeMKVs_name_is_still_recognised_as_this_rips_output()
    {
        //the bug: a crash left "title_t00.mkv" in the folder. The next rip overwrote it, the app thought
        //nothing new had appeared, and the finished disc vanished from the Ripped Discs tab.
        using var dir = new TempDir();
        var leftover = dir.File("title_t00.mkv", "half-written junk from a crash");
        File.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddMinutes(-10));

        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(args[args.Count - 1], "title_t00.mkv"), "the new rip");
            onLine?.Invoke("MSG:5036,0,1,\"Copy complete. 1 titles saved.\",\"x\"");
            return FakeRunner.Ok();
        });
        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { new TitleInfo { Id = 0 } }, DestDir = dir.Path, BaseName = "Movie" },
            null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { "Movie.mkv" }, result.Files);
        Assert.Equal("the new rip", File.ReadAllText(dir.Combine("Movie.mkv")));
    }

    /// <summary>Finishing without a new file is an error not a silent success.</summary>
    [Fact]
    public async Task Finishing_without_a_new_file_is_an_error_not_a_silent_success()
    {
        using var dir = new TempDir();
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            onLine?.Invoke("MSG:5036,0,1,\"Copy complete. 1 titles saved.\",\"x\"");
            return FakeRunner.Ok();   //...but writes nothing
        });
        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { new TitleInfo { Id = 0 } }, DestDir = dir.Path, BaseName = "Movie" },
            null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("no new MKV file appeared", result.Error);
    }

    /// <summary>Untouched files already in the folder are never claimed or removed.</summary>
    [Fact]
    public async Task Untouched_files_already_in_the_folder_are_never_claimed_or_removed()
    {
        using var dir = new TempDir();
        var other = dir.File("Another Movie.mkv", "someone else's rip");
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-3));
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(args[args.Count - 1], "title_t00.mkv"), "half");
            return FakeRunner.Exit(1);
        });
        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { new TitleInfo { Id = 0 } }, DestDir = dir.Path, BaseName = "Movie" },
            null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(new[] { "Another Movie.mkv" }, dir.FileNames());
    }

    #endregion
}

public class DebugDataTests
{
    #region Methods

    /// <summary>A history without a file lives only in memory.</summary>
    [Fact]
    public void A_history_without_a_file_lives_only_in_memory()
    {
        using var dir = new TempDir();
        dir.File("Movie.mkv");
        var store = new HistoryStore(null);
        var entry = store.BeginRip("M", "Movie", dir.Path, 1);
        store.FinishRip(entry, new[] { "Movie.mkv" }, 0);

        Assert.Single(store.Entries);
        Assert.Equal("Ripped", store.Entries[0].Status);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.json"));
    }

    /// <summary>A queue without a file lives only in memory.</summary>
    [Fact]
    public async Task A_queue_without_a_file_lives_only_in_memory()
    {
        using var dir = new TempDir();
        var sim = new SimulatedRunner { SpeedFactor = 0 };
        var queue = new CompressQueue(sim, null) { HandBrakePath = () => SimulatedRunner.FakeHandBrake };
        var done = new TaskCompletionSource();
        queue.JobFinished += _ => done.TrySetResult();
        queue.Load();   //nothing to load, must not throw

        queue.Add(new[] { dir.File("movie.mkv", "placeholder\nsize=1920x1080\n") }, dir.Path, "Fast 1080p30");
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.json"));
    }

    /// <summary>Old debug placeholders are cleaned up and nothing else is touched.</summary>
    [Fact]
    public void Old_debug_placeholders_are_cleaned_up_and_nothing_else_is_touched()
    {
        using var dir = new TempDir();
        dir.File("title_t00.mkv", SimulatedRunner.PlaceholderMarker + ". This is not a real video.\n");
        dir.File("Simulated Movie 1.mp4", SimulatedRunner.PlaceholderMarker + ". Simulated HandBrake encode.\n");
        dir.File("Old Name.mkv", "Easy Disc Ripper DEBUG MODE placeholder. This is not a real video.\n");
        dir.File("My Real Movie.mkv", "real video bytes that just happen to be here");
        dir.File("notes.txt", "not a placeholder");
        Directory.CreateDirectory(dir.Combine("sub"));
        File.WriteAllText(dir.Combine(Path.Combine("sub", "x.mkv")), SimulatedRunner.PlaceholderMarker + "\n");

        int removed = SimulatedRunner.CleanUpPlaceholders(dir.Path);

        Assert.Equal(4, removed);
        Assert.Equal(new[] { "My Real Movie.mkv", "notes.txt" }, dir.FileNames());
    }

    /// <summary>Cleaning a missing folder is harmless.</summary>
    [Fact]
    public void Cleaning_a_missing_folder_is_harmless()
        => Assert.Equal(0, SimulatedRunner.CleanUpPlaceholders(Path.Combine(Path.GetTempPath(), "erd-does-not-exist-" + Guid.NewGuid())));

    #endregion
}

public class DriveStatusTests
{
    #region Methods

    /// <summary>Lists real drives and what they are doing.</summary>
    [Fact]
    public void Lists_real_drives_and_what_they_are_doing()
    {
        var drives = MakeMkvParser.ParseDrives(new[]
        {
            "MSG:1005,0,1,\"MakeMKV started\",\"x\"",
            "DRV:0,0,999,0,\"BD-RE HL-DT-ST WH16NS40\",\"\",\"D:\"",
            "DRV:1,2,999,12,\"DVD+-RW TSSTcorp\",\"THE_MOVIE\",\"E:\"",
            "DRV:2,1,999,0,\"BD-ROM PIONEER\",\"\",\"F:\"",
            "DRV:3,256,999,0,\"\",\"\",\"\"",
        });

        Assert.Equal(3, drives.Count);
        Assert.Equal("BD-RE HL-DT-ST WH16NS40 (D:)", drives[0].Title);
        Assert.Equal("No disc in the drive", drives[0].StateText);
        Assert.Equal("Disc inserted: THE_MOVIE", drives[1].StateText);
        Assert.Equal("Tray is open", drives[2].StateText);
    }

    /// <summary>The watcher reports the drive every time it looks.</summary>
    [Fact]
    public async Task The_watcher_reports_the_drive_every_time_it_looks()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var sim = new SimulatedRunner { SpeedFactor = 0, Clock = () => now };
        var watcher = new DiscWatcher(sim, () => SimulatedRunner.FakeMakeMkv, TimeSpan.Zero);
        var seen = new List<string>();
        watcher.DrivesChanged += d => seen.Add(d.Single().Title + " | " + d.Single().StateText);

        await watcher.CheckDrivesAsync(CancellationToken.None);
        Assert.Equal("BD-RE SIMULATED DRIVE (SIM:) | No disc in the drive", seen[^1]);

        now = now.AddSeconds(5);
        await watcher.CheckDrivesAsync(CancellationToken.None);
        Assert.Equal("BD-RE SIMULATED DRIVE (SIM:) | Disc inserted: SIMULATED_MOVIE_1", seen[^1]);

        sim.Eject();
        await watcher.CheckDrivesAsync(CancellationToken.None);
        Assert.Equal("BD-RE SIMULATED DRIVE (SIM:) | Tray is open", seen[^1]);
    }

    #endregion
}
