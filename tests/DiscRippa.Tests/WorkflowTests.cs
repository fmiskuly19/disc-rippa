namespace DiscRippa.Tests;

public class DiscWatcherTests
{
    #region Properties

    private static readonly string[] TitleLines =
    {
        "TINFO:0,9,0,\"1:56:40\"", "TINFO:0,11,0,\"1000\"",
    };

    #endregion

    #region Methods

    /// <summary>
    /// A drive that shows these labels one after another (null = empty tray); the last one repeats.
    /// </summary>
    private static (FakeRunner Runner, Func<int> DriveChecks) Drive(params string?[] labels)
    {
        int checks = 0;
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999"))
            {
                var label = labels[Math.Min(checks, labels.Length - 1)];
                checks++;
                onLine?.Invoke(label == null
                    ? "DRV:0,0,999,0,\"\",\"\",\"\""
                    : $"DRV:0,2,999,12,\"BD-RE\",\"{label}\",\"D:\"");
            }
            else
            {
                foreach (var l in TitleLines) onLine?.Invoke(l);
            }
            return FakeRunner.Ok();
        });
        return (runner, () => checks);
    }

    /// <summary>A disc watcher using this fake MakeMKV.</summary>
    private static DiscWatcher Watcher(FakeRunner runner) => new(runner, () => "makemkvcon", TimeSpan.Zero) { RetryDelay = TimeSpan.Zero };

    /// <summary>Finds a disc and reads its titles.</summary>
    [Fact]
    public async Task Finds_a_disc_and_reads_its_titles()
    {
        var (runner, _) = Drive("MY_MOVIE");
        string? announced = null;
        var found = await Watcher(runner).WaitForDiscAsync(label => announced = label, CancellationToken.None);
        Assert.Equal("MY_MOVIE", found.DiscLabel);
        Assert.Equal("D:", found.Device);
        Assert.Equal("MY_MOVIE", announced);
        Assert.Single(found.Titles);
        Assert.Equal(7000, found.Titles[0].Seconds);
    }

    /// <summary>Waits through an empty tray.</summary>
    [Fact]
    public async Task Waits_through_an_empty_tray()
    {
        var (runner, checks) = Drive(null, null, "MY_MOVIE");
        var found = await Watcher(runner).WaitForDiscAsync(null, CancellationToken.None);
        Assert.Equal("MY_MOVIE", found.DiscLabel);
        Assert.Equal(3, checks());
    }

    /// <summary>Does not pick up the disc that was just finished.</summary>
    [Fact]
    public async Task Does_not_pick_up_the_disc_that_was_just_finished()
    {
        //a is still in the drive for two checks, then the tray is emptied, then A is put back in.
        var (runner, checks) = Drive("A", "A", null, "A");
        var watcher = Watcher(runner);
        watcher.Ignore = new DiscKey("A", "D:");

        var found = await watcher.WaitForDiscAsync(null, CancellationToken.None);

        Assert.Equal("A", found.DiscLabel);
        Assert.Equal(4, checks());          //it only accepted A after seeing the empty tray
        Assert.Null(watcher.Ignore);
    }

    /// <summary>Accepts a different disc straight away.</summary>
    [Fact]
    public async Task Accepts_a_different_disc_straight_away()
    {
        var (runner, checks) = Drive("A", "B");
        var watcher = Watcher(runner);
        watcher.Ignore = new DiscKey("A", "D:");
        var found = await watcher.WaitForDiscAsync(null, CancellationToken.None);
        Assert.Equal("B", found.DiscLabel);
        Assert.Equal(2, checks());
    }

    /// <summary>Pressing start by hand uses whatever is in the drive.</summary>
    [Fact]
    public async Task Pressing_start_by_hand_uses_whatever_is_in_the_drive()
    {
        var (runner, checks) = Drive("A");
        var found = await Watcher(runner).WaitForDiscAsync(null, CancellationToken.None);   //ignore is null
        Assert.Equal("A", found.DiscLabel);
        Assert.Equal(1, checks());
    }

    /// <summary>Can be cancelled while waiting.</summary>
    [Fact]
    public async Task Can_be_cancelled_while_waiting()
    {
        var (runner, _) = Drive((string?)null);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Watcher(runner).WaitForDiscAsync(null, cts.Token));
    }

    /// <summary>A disc with no titles is reported.</summary>
    [Fact]
    public async Task A_disc_with_no_titles_is_reported()
    {
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999")) onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"DIRTY\",\"D:\"");
            return FakeRunner.Ok();
        });
        await Assert.ThrowsAsync<DiscReadException>(
            () => Watcher(runner).WaitForDiscAsync(null, CancellationToken.None));
    }

    #endregion
}

public class RipSessionTests
{
    #region Methods

    /// <summary>A pretend title.</summary>
    private static TitleInfo Title(int id = 0) => new() { Id = id, Seconds = 7000, Duration = "1:56:40", Size = 1000 };

    /// <summary>A fake MakeMKV that "saves" a file for each title it is asked to rip.</summary>
    private static FakeRunner Maker(Func<int, string, IReadOnlyList<string>, Action<string>?, CancellationToken, Task<ProcessOutcome>> perTitle)
        => new((exe, args, onLine, ct) =>
        {
            //args: -r --progress=-same mkv disc:N <title> <dest>
            int title = int.Parse(args[args.Count - 2]);
            var dest = args[args.Count - 1];
            return perTitle(title, dest, args, onLine, ct);
        });

    /// <summary>Pretends MakeMKV saved this title successfully.</summary>
    private static Task<ProcessOutcome> SaveOk(int title, string dest, Action<string>? onLine)
    {
        File.WriteAllText(Path.Combine(dest, $"title_t{title:00}.mkv"), "video");
        onLine?.Invoke("PRGV:100,32768,65536");
        onLine?.Invoke("MSG:5005,0,0,\"Copy complete. 1 titles saved.\",\"x\"");
        return FakeRunner.Ok();
    }

    /// <summary>Rips one title and names the file.</summary>
    [Fact]
    public async Task Rips_one_title_and_names_the_file()
    {
        using var dir = new TempDir();
        var progress = new List<double>();
        var runner = Maker((t, dest, args, onLine, ct) => SaveOk(t, dest, onLine));

        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { DriveIndex = 0, Titles = new[] { Title() }, DestDir = dir.Path, BaseName = "My Movie" },
            p => progress.Add(p.Percent), null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new[] { "My Movie.mkv" }, result.Files);
        Assert.Equal(new[] { "My Movie.mkv" }, dir.FileNames());
        Assert.Contains(50.0, progress);
        Assert.Empty(result.Problems);
    }

    /// <summary>Never overwrites a file that is already there.</summary>
    [Fact]
    public async Task Never_overwrites_a_file_that_is_already_there()
    {
        using var dir = new TempDir();
        dir.File("My Movie.mkv", "old rip");
        var runner = Maker((t, dest, args, onLine, ct) => SaveOk(t, dest, onLine));

        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { Title() }, DestDir = dir.Path, BaseName = "My Movie" },
            null, null, CancellationToken.None);

        Assert.Equal(new[] { "My Movie (2).mkv" }, result.Files);
        Assert.Equal("old rip", File.ReadAllText(dir.Combine("My Movie.mkv")));
    }

    /// <summary>Several titles get numbered names.</summary>
    [Fact]
    public async Task Several_titles_get_numbered_names()
    {
        using var dir = new TempDir();
        var runner = Maker((t, dest, args, onLine, ct) => SaveOk(t, dest, onLine));

        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { Title(0), Title(1), Title(2) }, DestDir = dir.Path, BaseName = "Show" },
            null, null, CancellationToken.None);

        Assert.Equal(new[] { "Show - 01.mkv", "Show - 02.mkv", "Show - 03.mkv" }, result.Files);
    }

    /// <summary>Read errors are reported even when the rip finishes.</summary>
    [Fact]
    public async Task Read_errors_are_reported_even_when_the_rip_finishes()
    {
        using var dir = new TempDir();
        var counts = new List<int>();
        var runner = Maker((t, dest, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(dest, "title_t00.mkv"), "video");
            onLine?.Invoke("MSG:2003,0,0,\"Error 'Scsi error - MEDIUM ERROR:UNRECOVERED READ ERROR' occurred while reading 'x' at offset 100\",\"x\"");
            onLine?.Invoke("MSG:5005,0,0,\"Copy complete. 1 titles saved.\",\"x\"");
            return FakeRunner.Ok();
        });

        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { Title() }, DestDir = dir.Path, BaseName = "Scratched" },
            null, counts.Add, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.Problems);
        Assert.Equal(new[] { 1 }, counts);
        Assert.Equal(new[] { "Scratched.mkv" }, result.Files);   //the file is kept; the user is told it may have glitches
    }

    /// <summary>A failed rip removes only its own unfinished file.</summary>
    [Fact]
    public async Task A_failed_rip_removes_only_its_own_unfinished_file()
    {
        using var dir = new TempDir();
        dir.File("Someone Elses.mkv", "keep me");
        var runner = Maker((t, dest, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(dest, "title_t00.mkv"), "half");
            onLine?.Invoke("MSG:5003,0,0,\"Failed to save title 0 to file x\",\"x\"");
            onLine?.Invoke("MSG:5004,0,0,\"Copy complete. 0 titles saved, 1 failed.\",\"x\"");
            return FakeRunner.Exit(1);
        });

        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { Title() }, DestDir = dir.Path, BaseName = "Broken" },
            null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("could not finish", result.Error);
        Assert.Contains("Failed to save title 0", result.Error);
        Assert.Contains("unfinished file was removed", result.Error);
        Assert.Equal(new[] { "Someone Elses.mkv" }, dir.FileNames());
        Assert.Empty(result.Files);
    }

    /// <summary>A failure on a later title keeps the earlier ones.</summary>
    [Fact]
    public async Task A_failure_on_a_later_title_keeps_the_earlier_ones()
    {
        using var dir = new TempDir();
        var runner = Maker((t, dest, args, onLine, ct) =>
        {
            if (t == 0) return SaveOk(t, dest, onLine);
            File.WriteAllText(Path.Combine(dest, "title_t01.mkv"), "half");
            return FakeRunner.Exit(1);
        });

        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { Title(0), Title(1) }, DestDir = dir.Path, BaseName = "Disc" },
            null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(new[] { "Disc - 01.mkv" }, dir.FileNames());
        Assert.Equal(new[] { "Disc - 01.mkv" }, result.Files);
        Assert.Contains("The title ripped before this one was kept.", result.Error);
    }

    /// <summary>Aborting stops MakeMKV and removes the unfinished file.</summary>
    [Fact]
    public async Task Aborting_stops_MakeMKV_and_removes_the_unfinished_file()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        var runner = Maker(async (t, dest, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(dest, "title_t00.mkv"), "half");
            cts.CancelAfter(50);
            return await FakeRunner.RunUntilCancelled(ct);
        });

        var result = await new RipSession(runner, () => "makemkvcon").RunAsync(
            new RipRequest { Titles = new[] { Title() }, DestDir = dir.Path, BaseName = "Aborted" },
            null, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Null(result.Error);
        Assert.Equal(1, result.RemovedPartialFiles);
        Assert.Empty(dir.FileNames());
    }

    /// <summary>A missing MakeMKV is reported not thrown.</summary>
    [Fact]
    public async Task A_missing_MakeMKV_is_reported_not_thrown()
    {
        using var dir = new TempDir();
        var result = await new RipSession(Maker((t, d, a, o, c) => FakeRunner.Ok()), () => null).RunAsync(
            new RipRequest { Titles = new[] { Title() }, DestDir = dir.Path, BaseName = "x" },
            null, null, CancellationToken.None);
        Assert.Contains("MakeMKV wasn't found", result.Error);
    }

    /// <summary>Cleanup on demand removes a half written file.</summary>
    [Fact]
    public async Task Cleanup_on_demand_removes_a_half_written_file()
    {
        //used when the app window is closed in the middle of a rip.
        using var dir = new TempDir();
        var started = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();
        var runner = Maker(async (t, dest, args, onLine, ct) =>
        {
            File.WriteAllText(Path.Combine(dest, "title_t00.mkv"), "half");
            started.SetResult();
            return await FakeRunner.RunUntilCancelled(ct);
        });
        var session = new RipSession(runner, () => "makemkvcon");
        var run = session.RunAsync(
            new RipRequest { Titles = new[] { Title() }, DestDir = dir.Path, BaseName = "x" }, null, null, cts.Token);

        await started.Task;
        cts.Cancel();
        await run;
        Assert.Empty(dir.FileNames());
        Assert.Equal(0, session.RemovePartialFiles());   //calling it again is harmless
    }

    #endregion
}
