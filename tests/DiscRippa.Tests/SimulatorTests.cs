namespace DiscRippa.Tests;

public class SimulatorTests
{
    #region Methods

    /// <summary>A simulator with no delays.</summary>
    private static SimulatedRunner FastSim() => new() { SpeedFactor = 0, DiscSwapDelay = TimeSpan.Zero };

    /// <summary>A disc watcher using the simulator.</summary>
    private static DiscWatcher Watcher(SimulatedRunner sim) => new(sim, () => SimulatedRunner.FakeMakeMkv, TimeSpan.Zero);

    /// <summary>The simulator's HandBrake preset list.</summary>
    private static async Task<PresetCatalog> Presets(SimulatedRunner sim)
    {
        var lines = new List<string>();
        await sim.RunAsync(SimulatedRunner.FakeHandBrake, new[] { "--preset-list" }, lines.Add, CancellationToken.None);
        return PresetCatalog.Parse(string.Join("\n", lines));
    }

    /// <summary>Completes when <paramref name="count"/> jobs have ended.</summary>
    private static Task Finished(CompressQueue queue, int count)
    {
        var done = new TaskCompletionSource();
        int n = 0;
        queue.JobFinished += _ =>
        {
            if (Interlocked.Increment(ref n) >= count) done.TrySetResult();
        };
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>A disc goes in shortly after the app starts looking.</summary>
    [Fact]
    public void A_disc_goes_in_shortly_after_the_app_starts_looking()
    {
        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var sim = new SimulatedRunner { SpeedFactor = 0, Clock = () => now };
        string? Label()
        {
            var lines = new List<string>();
            sim.RunAsync("x", new[] { "-r", "--cache=1", "info", "disc:9999" }, lines.Add, CancellationToken.None).Wait();
            return MakeMkvParser.FindDisc(lines)?.DiscName;
        }

        Assert.Null(Label());                    //tray empty at first
        now = now.AddSeconds(2);
        Assert.Null(Label());
        now = now.AddSeconds(2);
        Assert.Equal("SIMULATED_MOVIE_1", Label());

        sim.Eject();                             //tray opens: disc gone, next one follows
        Assert.Null(Label());
        now = now.AddSeconds(4);
        Assert.Equal("SCRATCHED_DISC_2", Label());

        sim.DiscFinished();                      //finished without ejecting: "taken out by hand" a bit later
        Assert.Equal("SCRATCHED_DISC_2", Label());
        now = now.AddSeconds(4);
        Assert.Null(Label());
        now = now.AddSeconds(4);
        Assert.Equal("TV_SEASON_3", Label());
    }

    /// <summary>The whole workflow runs end to end without a disc.</summary>
    [Fact]
    public async Task The_whole_workflow_runs_end_to_end_without_a_disc()
    {
        using var dir = new TempDir();
        var sim = FastSim();
        var watcher = Watcher(sim);
        var queue = new CompressQueue(sim, dir.Combine("queue.json"))
        {
            HandBrakePath = () => SimulatedRunner.FakeHandBrake,
            Catalog = await Presets(sim),
        };

        var disc1 = await watcher.WaitForDiscAsync(null, CancellationToken.None);
        Assert.Equal("SIMULATED_MOVIE_1", disc1.DiscLabel);
        var main = disc1.Titles.OrderByDescending(t => t.Seconds).First();
        Assert.Equal("1:52:10", main.Duration);

        var progress = new List<double>();
        var rip1 = await new RipSession(sim, () => SimulatedRunner.FakeMakeMkv).RunAsync(
            new RipRequest { DriveIndex = disc1.DriveIndex, Titles = new[] { main }, DestDir = dir.Path, BaseName = "Test Movie" },
            p => progress.Add(p.Percent), null, CancellationToken.None);

        Assert.True(rip1.Success);
        Assert.Equal(new[] { "Test Movie.mkv" }, rip1.Files);
        Assert.Empty(rip1.Problems);
        Assert.True(progress.Count > 30);
        Assert.Equal(100.0, progress.Max(), 1);
        Assert.Contains("DEBUG MODE placeholder", File.ReadAllText(dir.Combine("Test Movie.mkv")));

        var wait1 = Finished(queue, 1);
        queue.Add(new[] { dir.Combine("Test Movie.mkv") }, dir.Path, DiscRippa.Core.Compression.Presets.Auto);
        await wait1;
        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);
        Assert.Equal("HQ 1080p30 Surround", queue.Jobs[0].Resolved);
        Assert.Equal(dir.Combine("Test Movie.mp4"), queue.Jobs[0].Output);

        sim.Eject();
        watcher.Ignore = new DiscKey(disc1.DiscLabel, disc1.Device);
        var disc2 = await watcher.WaitForDiscAsync(null, CancellationToken.None);
        Assert.Equal("SCRATCHED_DISC_2", disc2.DiscLabel);

        var problemCounts = new List<int>();
        var rip2 = await new RipSession(sim, () => SimulatedRunner.FakeMakeMkv).RunAsync(
            new RipRequest { Titles = new[] { disc2.Titles.OrderByDescending(t => t.Seconds).First() }, DestDir = dir.Path, BaseName = "Scratched" },
            null, problemCounts.Add, CancellationToken.None);
        Assert.True(rip2.Success);                 //finishes, but with warnings
        Assert.Equal(2, rip2.Problems.Count);
        Assert.Equal(new[] { 1, 2 }, problemCounts);

        sim.Eject();
        watcher.Ignore = new DiscKey(disc2.DiscLabel, disc2.Device);
        var disc3 = await watcher.WaitForDiscAsync(null, CancellationToken.None);
        Assert.Equal("TV_SEASON_3", disc3.DiscLabel);
        var episodes = disc3.Titles.Where(t => t.Seconds >= 120).ToArray();
        Assert.Equal(4, episodes.Length);

        var rip3 = await new RipSession(sim, () => SimulatedRunner.FakeMakeMkv).RunAsync(
            new RipRequest { Titles = episodes, DestDir = dir.Path, BaseName = "Show" }, null, null, CancellationToken.None);
        Assert.Equal(new[] { "Show - 01 (VTS_01_1.VOB).mkv", "Show - 02 (VTS_02_1.VOB).mkv", "Show - 03 (VTS_03_1.VOB).mkv", "Show - 04 (VTS_04_1.VOB).mkv" },
                     rip3.Files);

        var wait3 = Finished(queue, 4);
        queue.Add(rip3.Files.Select(dir.Combine), dir.Path, DiscRippa.Core.Compression.Presets.Auto);
        await wait3;
        Assert.All(queue.Jobs.Skip(1), j => Assert.Equal("HQ 480p30 Surround", j.Resolved));
        Assert.All(queue.Jobs, j => Assert.Equal(JobStatus.Done, j.Status));
    }

    /// <summary>Aborting a simulated rip works like a real one.</summary>
    [Fact]
    public async Task Aborting_a_simulated_rip_works_like_a_real_one()
    {
        using var dir = new TempDir();
        var sim = new SimulatedRunner { SpeedFactor = 1, DiscSwapDelay = TimeSpan.Zero };   //real speed, so we can abort mid-way
        var disc = await Watcher(sim).WaitForDiscAsync(null, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        var result = await new RipSession(sim, () => SimulatedRunner.FakeMakeMkv).RunAsync(
            new RipRequest { Titles = new[] { disc.Titles[0] }, DestDir = dir.Path, BaseName = "Aborted" }, null, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(1, result.RemovedPartialFiles);
        Assert.Empty(dir.FileNames());
    }

    /// <summary>Stopping a simulated compression removes its output.</summary>
    [Fact]
    public async Task Stopping_a_simulated_compression_removes_its_output()
    {
        using var dir = new TempDir();
        var sim = new SimulatedRunner { SpeedFactor = 1 };
        var queue = new CompressQueue(sim, dir.Combine("queue.json"))
        {
            HandBrakePath = () => SimulatedRunner.FakeHandBrake,
            Catalog = await Presets(sim),
        };
        var file = dir.File("movie.mkv", "placeholder\nsize=1920x1080\n");
        var done = Finished(queue, 1);

        queue.Add(new[] { file }, dir.Path, "Fast 1080p30");
        await Task.Delay(800);
        Assert.True(queue.Jobs[0].Percent > 0);    //progress is moving
        queue.StopCurrent();
        await done;

        Assert.Equal(JobStatus.Cancelled, queue.Jobs[0].Status);
        Assert.False(File.Exists(dir.Combine("movie.mp4")));
        Assert.True(File.Exists(file));
    }

    /// <summary>Its preset list parses like the real one.</summary>
    [Fact]
    public async Task Its_preset_list_parses_like_the_real_one()
    {
        var catalog = await Presets(FastSim());
        Assert.Contains("HQ 1080p30 Surround", catalog.Names);
        Assert.Contains("HQ 480p30 Surround", catalog.Names);
        Assert.Equal(".mkv", catalog.ExtensionFor("H.265 MKV 1080p30"));
        Assert.DoesNotContain("CLI Default", catalog.Names);
    }

    #endregion
}
