namespace DiscRippa.Tests;

public class ExclusiveRunnerTests
{
    #region Methods

    /// <summary>Only one MakeMKV runs at a time.</summary>
    [Fact]
    public async Task Only_one_MakeMKV_runs_at_a_time()
    {
        int running = 0, maxRunning = 0;
        var gate = new object();
        var inner = new FakeRunner(async (exe, args, onLine, ct) =>
        {
            int now = Interlocked.Increment(ref running);
            lock (gate) maxRunning = Math.Max(maxRunning, now);
            await Task.Delay(50);
            Interlocked.Decrement(ref running);
            return new ProcessOutcome();
        });
        var exclusive = new ExclusiveRunner(inner);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Task.Run(() => exclusive.RunAsync("makemkvcon", new[] { "info" }, null, CancellationToken.None))));

        Assert.Equal(1, maxRunning);
        Assert.Equal(6, inner.Calls.Count);
    }

    /// <summary>Giving up while waiting for a turn reports cancelled instead of throwing.</summary>
    [Fact]
    public async Task Giving_up_while_waiting_for_a_turn_reports_cancelled_instead_of_throwing()
    {
        var release = new TaskCompletionSource();
        var inner = new FakeRunner(async (exe, args, onLine, ct) =>
        {
            await release.Task;
            return new ProcessOutcome();
        });
        var exclusive = new ExclusiveRunner(inner);
        var first = exclusive.RunAsync("makemkvcon", new[] { "a" }, null, CancellationToken.None);

        using var cts = new CancellationTokenSource(100);
        var second = await exclusive.RunAsync("makemkvcon", new[] { "b" }, null, cts.Token);

        Assert.True(second.Cancelled);
        release.SetResult();
        await first;
    }

    #endregion
}

public class DiscReadingTests
{
    #region Methods

    /// <summary>A read that comes back empty is retried before giving up.</summary>
    [Fact]
    public async Task A_read_that_comes_back_empty_is_retried_before_giving_up()
    {
        int reads = 0;
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999"))
            {
                onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"YES_DISC_1\",\"G:\"");
                return FakeRunner.Ok();
            }
            reads++;
            if (reads == 1)
            {
                onLine?.Invoke("MSG:2003,0,0,\"Error 'Scsi error - NOT READY:LOGICAL UNIT IS IN PROCESS OF BECOMING READY'\",\"x\"");
                return FakeRunner.Ok();   //drive still spinning up: nothing read
            }
            onLine?.Invoke("TINFO:0,9,0,\"2:47:27\"");
            return FakeRunner.Ok();
        });
        var messages = new List<string>();
        var watcher = new DiscWatcher(runner, () => "makemkvcon", TimeSpan.Zero) { RetryDelay = TimeSpan.Zero };

        var disc = await watcher.WaitForDiscAsync(null, CancellationToken.None, messages.Add);

        Assert.Equal(2, reads);
        Assert.Single(disc.Titles);
        Assert.Contains(messages, m => m.Contains("BECOMING READY"));
        Assert.Contains(messages, m => m.Contains("Trying again (2 of 3)"));
    }

    /// <summary>Giving up explains what MakeMKV said.</summary>
    [Fact]
    public async Task Giving_up_explains_what_MakeMKV_said()
    {
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999")) onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"DIRTY\",\"G:\"");
            else onLine?.Invoke("MSG:5010,0,0,\"Failed to open disc\",\"x\"");
            return FakeRunner.Ok();
        });
        var watcher = new DiscWatcher(runner, () => "makemkvcon", TimeSpan.Zero) { RetryDelay = TimeSpan.Zero };

        var error = await Assert.ThrowsAsync<DiscReadException>(() => watcher.WaitForDiscAsync(null, CancellationToken.None));

        Assert.Contains("No titles could be read", error.Message);
        Assert.Contains("Failed to open disc", error.Message);
        Assert.Equal(3, runner.Calls.Count(c => c.Contains("disc:0")));
    }

    /// <summary>A drive that vanishes mid read is explained as a drive problem not a dirty disc.</summary>
    [Fact]
    public async Task A_drive_that_vanishes_mid_read_is_explained_as_a_drive_problem_not_a_dirty_disc()
    {
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999")) onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"MOVIE\",\"G:\"");
            else
            {
                onLine?.Invoke("MSG:5010,0,0,\"Failed to open disc\",\"x\"");
                onLine?.Invoke("MSG:2024,0,0,\"The program can't find any usable optical drives.\",\"x\"");
            }
            return FakeRunner.Ok();
        });
        var watcher = new DiscWatcher(runner, () => "makemkvcon", TimeSpan.Zero) { RetryDelay = TimeSpan.Zero };

        var error = await Assert.ThrowsAsync<DiscReadException>(() => watcher.WaitForDiscAsync(null, CancellationToken.None));

        Assert.Contains("drive disappeared", error.Message);
        Assert.DoesNotContain("dirty", error.Message);
    }

    /// <summary>A Blu ray blocked by MakeMKVs evaluation prompt says what to do.</summary>
    [Fact]
    public async Task A_Blu_ray_blocked_by_MakeMKVs_evaluation_prompt_says_what_to_do()
    {
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999")) onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"MOVIE\",\"G:\"");
            else
            {
                onLine?.Invoke("MSG:5021,0,0,\"This functionality is shareware. You may evaluate it for 30 days after what you would need to purchase an activation key if you like the functionality. Do you want to start evaluation period now?\",\"x\"");
                onLine?.Invoke("MSG:5010,0,0,\"Failed to open disc\",\"x\"");
            }
            return FakeRunner.Ok();
        });
        var watcher = new DiscWatcher(runner, () => "makemkvcon", TimeSpan.Zero) { RetryDelay = TimeSpan.Zero };

        var error = await Assert.ThrowsAsync<DiscReadException>(() => watcher.WaitForDiscAsync(null, CancellationToken.None));

        Assert.Contains("Activate Blu-ray", error.Message);
        Assert.DoesNotContain("dirty", error.Message);
    }

    /// <summary>Reading reports MakeMKVs messages and progress as it goes.</summary>
    [Fact]
    public async Task Reading_reports_MakeMKVs_messages_and_progress_as_it_goes()
    {
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999"))
            {
                onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"YES_DISC_1\",\"G:\"");
                return FakeRunner.Ok();
            }
            Assert.Contains("--progress=-same", args);
            onLine?.Invoke("MSG:3307,0,0,\"Complex multiplex encountered - 19 cells and 2152 VOBUs have to be scanned.\",\"x\"");
            onLine?.Invoke("PRGV:0,32768,65536");
            onLine?.Invoke("TINFO:0,9,0,\"2:47:27\"");
            return FakeRunner.Ok();
        });
        var messages = new List<string>();
        var progress = new List<double>();
        await new DiscWatcher(runner, () => "makemkvcon", TimeSpan.Zero)
            .WaitForDiscAsync(null, CancellationToken.None, messages.Add, progress.Add);

        Assert.Contains(messages, m => m.StartsWith("Complex multiplex"));
        Assert.Equal(new[] { 50.0 }, progress);
    }

    /// <summary>A drive check that gets no answer just looks again.</summary>
    [Fact]
    public async Task A_drive_check_that_gets_no_answer_just_looks_again()
    {
        int checks = 0;
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            if (args.Contains("disc:9999"))
            {
                checks++;
                if (checks == 1) return Task.FromResult(new ProcessOutcome { Cancelled = true, ExitCode = -1 });   //busy: no output
                onLine?.Invoke("DRV:0,2,999,12,\"BD-RE\",\"YES_DISC_1\",\"G:\"");
                return FakeRunner.Ok();
            }
            onLine?.Invoke("TINFO:0,9,0,\"2:47:27\"");
            return FakeRunner.Ok();
        });
        var disc = await new DiscWatcher(runner, () => "makemkvcon", TimeSpan.Zero).WaitForDiscAsync(null, CancellationToken.None);
        Assert.Equal(2, checks);
        Assert.Equal("YES_DISC_1", disc.DiscLabel);
    }

    #endregion
}
