using System.Text.Json;

namespace DiscRippa.Tests;

public class CompressQueueTests
{
    #region Methods

    /// <summary>The value after a command-line flag, or "".</summary>
    private static string Arg(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count - 1; i++)
            if (args[i] == name) return args[i + 1];
        return "";
    }

    /// <summary>True for HandBrake's scan command.</summary>
    private static bool IsScan(IReadOnlyList<string> args) => args.Contains("-t");

    /// <summary>A queue using this fake HandBrake, saving into <paramref name="dir"/>.</summary>
    private static CompressQueue NewQueue(IProcessRunner runner, TempDir dir)
        => new(runner, dir.Combine("queue.json"))
        {
            HandBrakePath = () => "HandBrakeCLI",
            Catalog = Fixture.Catalog,
        };

    /// <summary>Completes when <paramref name="count"/> jobs have ended.</summary>
    private static Task Finished(CompressQueue queue, int count)
    {
        var done = new TaskCompletionSource();
        int n = 0;
        queue.JobFinished += _ =>
        {
            if (Interlocked.Increment(ref n) >= count) done.TrySetResult();
        };
        return done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    /// <summary>Compresses files one at a time in order and picks the preset for each.</summary>
    [Fact]
    public async Task Compresses_files_one_at_a_time_in_order_and_picks_the_preset_for_each()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        var wait = Finished(queue, 2);

        queue.Add(new[] { dir.File("bluray.mkv", "1920x1080"), dir.File("dvd.mkv", "720x480") }, null, Presets.Auto);
        await wait;

        Assert.All(queue.Jobs, j => Assert.Equal(JobStatus.Done, j.Status));
        Assert.Equal(1, hb.MaxConcurrent);
        Assert.Equal(new[] { "HQ 1080p30 Surround", "HQ 480p30 Surround" }, hb.Presets);
        Assert.Equal("HQ 1080p30 Surround", queue.Jobs[0].Resolved);
        Assert.Equal("Source is 1920x1080.", queue.Jobs[0].Note);
        Assert.Equal("Auto: HQ 1080p30 Surround", queue.Jobs[0].QualityText);
        Assert.Equal(new[] { "bluray.mkv", "bluray.mp4", "dvd.mkv", "dvd.mp4", "queue.json" }, dir.FileNames());
        Assert.Equal("mkv", Path.GetExtension(queue.Jobs[0].Path).TrimStart('.'));   //originals untouched
        Assert.Equal("1920x1080", File.ReadAllText(dir.Combine("bluray.mkv")));
    }

    /// <summary>Output goes to the folder that was chosen.</summary>
    [Fact]
    public async Task Output_goes_to_the_folder_that_was_chosen()
    {
        using var dir = new TempDir();
        using var other = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("movie.mkv", "1920x1080") }, other.Path, Presets.Auto);
        await wait;

        Assert.Equal(other.Combine("movie.mp4"), queue.Jobs[0].Output);
        Assert.True(File.Exists(other.Combine("movie.mp4")));
        Assert.True(File.Exists(dir.Combine("movie.mkv")));
    }

    /// <summary>An explicit preset skips the scan and picks the matching file type.</summary>
    [Fact]
    public async Task An_explicit_preset_skips_the_scan_and_picks_the_matching_file_type()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("film.mkv", "1920x1080") }, null, "H.265 MKV 1080p30");
        await wait;

        Assert.Equal(0, hb.ScanCount);
        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);
        //an MKV preset on an MKV source must never overwrite the original
        Assert.Equal(dir.Combine("film (compressed).mkv"), queue.Jobs[0].Output);
        Assert.Equal("1920x1080", File.ReadAllText(dir.Combine("film.mkv")));
    }

    /// <summary>Unreadable resolution falls back to 1080p and says so.</summary>
    [Fact]
    public async Task Unreadable_resolution_falls_back_to_1080p_and_says_so()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("weird.mkv", "unreadable") }, null, Presets.Auto);
        await wait;

        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);
        Assert.Equal("HQ 1080p30 Surround", queue.Jobs[0].Resolved);
        Assert.Contains("Couldn't read", queue.Jobs[0].Note);
    }

    /// <summary>A failed job explains itself and leaves no partial file.</summary>
    [Fact]
    public async Task A_failed_job_explains_itself_and_leaves_no_partial_file()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake { ExitCodeFor = _ => 3 };
        var queue = NewQueue(hb.Runner, dir);
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("bad.mkv", "1920x1080") }, null, "Fast 1080p30");
        await wait;

        var job = queue.Jobs[0];
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("original kept", job.Error);
        Assert.Contains("scan thread found 0 valid title(s)", job.Error);
        Assert.Contains("Command:", job.Error);
        Assert.Contains("--preset \"Fast 1080p30\"", job.Error);
        Assert.Equal(new[] { "bad.mkv", "queue.json" }, dir.FileNames());
        Assert.Equal(1, queue.FailedCount);
    }

    /// <summary>A failed job does not block the next one and can be retried.</summary>
    [Fact]
    public async Task A_failed_job_does_not_block_the_next_one_and_can_be_retried()
    {
        using var dir = new TempDir();
        bool failFirst = true;
        var hb = new FakeHandBrake { ExitCodeFor = args => failFirst && Arg(args, "-i").EndsWith("a.mkv") ? 3 : 0 };
        var queue = NewQueue(hb.Runner, dir);
        var wait = Finished(queue, 2);

        queue.Add(new[] { dir.File("a.mkv", "1920x1080"), dir.File("b.mkv", "1920x1080") }, null, "Fast 1080p30");
        await wait;

        Assert.Equal(JobStatus.Failed, queue.Jobs[0].Status);
        Assert.Equal(JobStatus.Done, queue.Jobs[1].Status);

        failFirst = false;
        var again = Finished(queue, 1);
        queue.Retry(queue.Jobs[0]);
        await again;
        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);
        Assert.Equal("", queue.Jobs[0].Error);
        Assert.True(File.Exists(dir.Combine("a.mp4")));
    }

    /// <summary>Stopping the current job removes its unfinished file and moves on.</summary>
    [Fact]
    public async Task Stopping_the_current_job_removes_its_unfinished_file_and_moves_on()
    {
        using var dir = new TempDir();
        var started = new TaskCompletionSource();
        int encodes = 0;
        var runner = new FakeRunner(async (exe, args, onLine, ct) =>
        {
            if (IsScan(args)) return new ProcessOutcome();
            File.WriteAllText(Arg(args, "-o"), "partial");
            if (Interlocked.Increment(ref encodes) == 1)
            {
                started.TrySetResult();
                return await FakeRunner.RunUntilCancelled(ct);
            }
            return new ProcessOutcome { ExitCode = 0 };
        });
        var queue = NewQueue(runner, dir);
        var wait = Finished(queue, 2);

        queue.Add(new[] { dir.File("first.mkv", "1920x1080"), dir.File("second.mkv", "1920x1080") }, null, "Fast 1080p30");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        queue.StopCurrent();
        await wait;

        Assert.Equal(JobStatus.Cancelled, queue.Jobs[0].Status);
        Assert.False(File.Exists(dir.Combine("first.mp4")));
        Assert.True(File.Exists(dir.Combine("first.mkv")));
        Assert.Equal(JobStatus.Done, queue.Jobs[1].Status);
    }

    /// <summary>Pausing holds back new files until resumed.</summary>
    [Fact]
    public async Task Pausing_holds_back_new_files_until_resumed()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        queue.SetPaused(true);
        queue.Add(new[] { dir.File("held.mkv", "1920x1080") }, null, "Fast 1080p30");

        await Task.Delay(200);
        Assert.Equal(JobStatus.Queued, queue.Jobs[0].Status);
        Assert.Equal("Queue paused.", queue.CurrentText);

        var wait = Finished(queue, 1);
        queue.SetPaused(false);
        await wait;
        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);
    }

    /// <summary>The same file is not queued twice while it is waiting.</summary>
    [Fact]
    public void The_same_file_is_not_queued_twice_while_it_is_waiting()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        queue.SetPaused(true);
        var file = dir.File("dup.mkv", "1920x1080");

        Assert.Equal(1, queue.Add(new[] { file, file }, null, "Fast 1080p30"));
        Assert.Equal(0, queue.Add(new[] { file }, null, "Fast 1080p30"));
        Assert.Single(queue.Jobs);
        Assert.Equal(1, queue.PendingCount);
    }

    /// <summary>A missing source file fails cleanly.</summary>
    [Fact]
    public async Task A_missing_source_file_fails_cleanly()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        var wait = Finished(queue, 1);
        queue.Add(new[] { dir.Combine("gone.mkv") }, null, "Fast 1080p30");
        await wait;
        Assert.Equal(JobStatus.Failed, queue.Jobs[0].Status);
        Assert.Contains("no longer exists", queue.Jobs[0].Error);
    }

    /// <summary>Nothing runs without HandBrake.</summary>
    [Fact]
    public void Nothing_runs_without_HandBrake()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        queue.HandBrakePath = () => null;
        queue.Add(new[] { dir.File("x.mkv", "1920x1080") }, null, "Fast 1080p30");
        Assert.Equal(JobStatus.Queued, queue.Jobs[0].Status);
        Assert.Empty(hb.Presets);
    }

    /// <summary>Remove and clear finished.</summary>
    [Fact]
    public async Task Remove_and_clear_finished()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        var wait = Finished(queue, 2);
        queue.Add(new[] { dir.File("a.mkv", "1920x1080"), dir.File("b.mkv", "1920x1080") }, null, "Fast 1080p30");
        await wait;

        Assert.True(queue.Remove(queue.Jobs[0]));
        Assert.Single(queue.Jobs);
        queue.ClearFinished();
        Assert.Empty(queue.Jobs);
    }

    /// <summary>The list survives a restart and a half done job starts over.</summary>
    [Fact]
    public async Task The_list_survives_a_restart_and_a_half_done_job_starts_over()
    {
        using var dir = new TempDir();
        var file = dir.File("movie.mkv", "1920x1080");
        var saved = JsonSerializer.Serialize(new[]
        {
            new { Id = 7, Path = file, OutDir = dir.Path, Preset = "Fast 1080p30", Status = "Compressing", Output = "", Error = "" },
            new { Id = 9, Path = "x.mkv", OutDir = dir.Path, Preset = "Fast 1080p30", Status = "Failed", Output = "", Error = "boom" },
        });
        File.WriteAllText(dir.Combine("queue.json"), saved);

        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        var wait = Finished(queue, 1);
        queue.Load();
        queue.Pump();
        await wait;

        Assert.Equal(2, queue.Jobs.Count);
        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);   //was "Compressing" when saved, ran again
        Assert.Equal(JobStatus.Failed, queue.Jobs[1].Status);
        Assert.Equal("boom", queue.Jobs[1].Error);

        queue.SetPaused(true);
        queue.Add(new[] { dir.File("next.mkv", "1920x1080") }, null, "Fast 1080p30");
        Assert.Equal(10, queue.Jobs[2].Id);                    //ids keep counting up
    }

    /// <summary>Closing the app mid job removes the half written file.</summary>
    [Fact]
    public async Task Closing_the_app_mid_job_removes_the_half_written_file()
    {
        using var dir = new TempDir();
        var started = new TaskCompletionSource();
        var runner = new FakeRunner(async (exe, args, onLine, ct) =>
        {
            if (IsScan(args)) return new ProcessOutcome();
            File.WriteAllText(Arg(args, "-o"), "partial");
            started.TrySetResult();
            return await FakeRunner.RunUntilCancelled(ct);
        });
        var queue = NewQueue(runner, dir);
        queue.Add(new[] { dir.File("movie.mkv", "1920x1080") }, null, "Fast 1080p30");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        queue.Shutdown();

        Assert.False(File.Exists(dir.Combine("movie.mp4")));
        Assert.True(File.Exists(dir.Combine("movie.mkv")));
    }

    /// <summary>The original is deleted after a successful compression when asked.</summary>
    [Fact]
    public async Task The_original_is_deleted_after_a_successful_compression_when_asked()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        var wait = Finished(queue, 1);
        var mkv = dir.File("Movie.mkv", "1920x1080");

        queue.Add(new[] { mkv }, null, "Fast 1080p30", deleteOriginal: true);
        await wait;

        var job = queue.Jobs[0];
        Assert.Equal(JobStatus.Done, job.Status);
        Assert.False(File.Exists(mkv));
        Assert.True(File.Exists(job.Output));
        Assert.True(job.OriginalDeleted);
        Assert.Contains("deleted", job.Detail);
    }

    /// <summary>Without asking the original is kept.</summary>
    [Fact]
    public async Task Without_asking_the_original_is_kept()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        var wait = Finished(queue, 1);
        var mkv = dir.File("Movie.mkv", "1920x1080");

        queue.Add(new[] { mkv }, null, "Fast 1080p30");
        await wait;

        Assert.True(File.Exists(mkv));
        Assert.False(queue.Jobs[0].OriginalDeleted);
    }

    /// <summary>A failed compression never deletes the original.</summary>
    [Fact]
    public async Task A_failed_compression_never_deletes_the_original()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake { ExitCodeFor = _ => 3 }.Runner, dir);
        var wait = Finished(queue, 1);
        var mkv = dir.File("Movie.mkv", "1920x1080");

        queue.Add(new[] { mkv }, null, "Fast 1080p30", deleteOriginal: true);
        await wait;

        Assert.Equal(JobStatus.Failed, queue.Jobs[0].Status);
        Assert.True(File.Exists(mkv));
    }

    /// <summary>An empty or tiny compressed file never replaces the original.</summary>
    [Fact]
    public async Task An_empty_or_tiny_compressed_file_never_replaces_the_original()
    {
        using var dir = new TempDir();
        var runner = new FakeRunner((exe, args, onLine, ct) =>
        {
            File.WriteAllBytes(Arg(args, "-o"), new byte[10]);   //"succeeded", but 10 bytes from a big file
            return FakeRunner.Ok();
        });
        var queue = NewQueue(runner, dir);
        var wait = Finished(queue, 1);
        var mkv = dir.File("Movie.mkv", new string('x', 500_000));

        queue.Add(new[] { mkv }, null, "Fast 1080p30", deleteOriginal: true);
        await wait;

        Assert.True(File.Exists(mkv));
        Assert.False(queue.Jobs[0].OriginalDeleted);
        Assert.Contains("too small", queue.Jobs[0].Detail);
    }

    /// <summary>The delete choice survives a restart.</summary>
    [Fact]
    public void The_delete_choice_survives_a_restart()
    {
        using var dir = new TempDir();
        var file = dir.Combine("queue.json");
        var hold = new FakeRunner((exe, args, onLine, ct) => FakeRunner.RunUntilCancelled(ct));
        var queue = new CompressQueue(hold, file) { HandBrakePath = () => null };
        queue.Add(new[] { dir.File("Movie.mkv", "1920x1080") }, null, "Fast 1080p30", deleteOriginal: true);

        var again = new CompressQueue(hold, file) { HandBrakePath = () => null };
        again.Load();
        Assert.True(again.Jobs.Single().DeleteOriginal);
    }

    /// <summary>Auto uses the graphics card when HandBrake has one.</summary>
    [Fact]
    public async Task Auto_uses_the_graphics_card_when_HandBrake_has_one()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        queue.HardwareEncoders = new[] { "nvenc" };
        queue.PreferGraphicsCard = () => true;
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("movie.mkv", "1920x1080") }, null, Presets.Auto);
        await wait;

        Assert.Equal(JobStatus.Done, queue.Jobs[0].Status);
        Assert.Equal(new[] { "H.265 NVENC 1080p" }, hb.Presets);
        Assert.Contains("graphics card (NVIDIA NVENC)", queue.Jobs[0].Detail);

        //the surround track is kept: stereo AAC plus 5.1 AC-3, given after --preset so they override it.
        var args = hb.EncodeArgs[0].ToList();
        Assert.True(args.IndexOf("--aencoder") > args.IndexOf("--preset"));
        Assert.Equal("av_aac,copy:ac3", Arg(args, "--aencoder"));
        Assert.Equal("stereo,5point1", Arg(args, "--mixdown"));
        Assert.Equal("ac3", Arg(args, "--audio-fallback"));
    }

    /// <summary>A failed graphics card encode is redone on the CPU.</summary>
    [Fact]
    public async Task A_failed_graphics_card_encode_is_redone_on_the_CPU()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake { ExitCodeFor = args => Arg(args, "--preset").Contains("NVENC") ? 1 : 0 };
        var queue = NewQueue(hb.Runner, dir);
        queue.HardwareEncoders = new[] { "nvenc" };
        queue.PreferGraphicsCard = () => true;
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("movie.mkv", "1920x1080") }, null, Presets.Auto);
        await wait;

        var job = queue.Jobs[0];
        Assert.Equal(JobStatus.Done, job.Status);
        Assert.Equal(new[] { "H.265 NVENC 1080p", "HQ 1080p30 Surround" }, hb.Presets);
        Assert.DoesNotContain("--aencoder", hb.EncodeArgs[1]);   //the HQ Surround preset already has both tracks
        Assert.Equal("HQ 1080p30 Surround", job.Resolved);
        Assert.Contains("done with HQ 1080p30 Surround on the CPU", job.Detail);
        Assert.True(File.Exists(job.Output));
    }

    /// <summary>With the option off or no graphics card Auto uses the CPU.</summary>
    [Fact]
    public async Task With_the_option_off_or_no_graphics_card_Auto_uses_the_CPU()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        queue.HardwareEncoders = new[] { "nvenc" };
        queue.PreferGraphicsCard = () => false;
        var wait = Finished(queue, 2);

        queue.Add(new[] { dir.File("a.mkv", "1920x1080") }, null, Presets.Auto);
        await Task.Delay(50);
        queue.PreferGraphicsCard = () => true;
        queue.HardwareEncoders = Array.Empty<string>();
        queue.Add(new[] { dir.File("b.mkv", "720x480") }, null, Presets.Auto);
        await wait;

        Assert.Equal(new[] { "HQ 1080p30 Surround", "HQ 480p30 Surround" }, hb.Presets);
    }

    /// <summary>MKV copies keep every audio track and subtitle untouched.</summary>
    [Fact]
    public async Task MKV_copies_keep_every_audio_track_and_subtitle_untouched()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        queue.HardwareEncoders = new[] { "nvenc" };
        queue.PreferGraphicsCard = () => true;
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("Movie.mkv", "1920x1080") }, null, Presets.Auto, saveAsMkv: true);
        await wait;

        var job = queue.Jobs[0];
        var args = hb.EncodeArgs[0].ToList();
        Assert.Equal(JobStatus.Done, job.Status);
        Assert.Equal("av_mkv", Arg(args, "--format"));
        Assert.Equal("copy", Arg(args, "--aencoder"));
        Assert.Contains("--all-audio", args);
        Assert.Contains("--all-subtitles", args);
        Assert.Contains("truehd", Arg(args, "--audio-copy-mask"));
        Assert.Equal(dir.Combine("Movie (compressed).mkv"), job.Output);   //never on top of the original
        Assert.Equal("1920x1080", File.ReadAllText(dir.Combine("Movie.mkv")));
    }

    /// <summary>When the MKV original is deleted the compressed copy takes its name.</summary>
    [Fact]
    public async Task When_the_MKV_original_is_deleted_the_compressed_copy_takes_its_name()
    {
        using var dir = new TempDir();
        var queue = NewQueue(new FakeHandBrake().Runner, dir);
        var wait = Finished(queue, 1);

        queue.Add(new[] { dir.File("Movie.mkv", "1920x1080") }, null, "Fast 1080p30", deleteOriginal: true, saveAsMkv: true);
        await wait;

        Assert.Equal(dir.Combine("Movie.mkv"), queue.Jobs[0].Output);
        Assert.Equal("encoded", File.ReadAllText(dir.Combine("Movie.mkv")));
        Assert.Equal(new[] { "Movie.mkv", "queue.json" }, dir.FileNames());
    }

    /// <summary>MP4 stays the default.</summary>
    [Fact]
    public async Task MP4_stays_the_default()
    {
        using var dir = new TempDir();
        var hb = new FakeHandBrake();
        var queue = NewQueue(hb.Runner, dir);
        var wait = Finished(queue, 1);
        queue.Add(new[] { dir.File("Movie.mkv", "1920x1080") }, null, "Fast 1080p30");
        await wait;
        Assert.DoesNotContain("--format", hb.EncodeArgs[0]);
        Assert.EndsWith(".mp4", queue.Jobs[0].Output);
    }

    /// <summary>Without a graphics card there is no hardware preset.</summary>
    [Fact]
    public void Without_a_graphics_card_there_is_no_hardware_preset()
        => Assert.Null(HandBrake.ChooseHardwarePreset(1080, 1920, Array.Empty<string>(), Fixture.Catalog.Names.ToList()));

    #endregion

    #region Nested types

    /// <summary>
    /// A fake HandBrake. Scanning reports the size written inside the source file (e.g. "1920x1080");
    /// encoding writes the output file and prints progress.
    /// </summary>
    private sealed class FakeHandBrake
    {
        #region Fields

        private int _running;
        public int MaxConcurrent;
        public List<string> Presets = new();
        public int ScanCount;
        public Func<IReadOnlyList<string>, int>? ExitCodeFor;
        public List<string[]> EncodeArgs = new();

        #endregion

        #region Properties

        public FakeRunner Runner => new(async (exe, args, onLine, ct) =>
        {
            if (IsScan(args))
            {
                Interlocked.Increment(ref ScanCount);
                onLine?.Invoke("+ title 1:");
                var size = File.ReadAllText(Arg(args, "-i"));
                if (size != "unreadable") onLine?.Invoke($"  + size: {size}, pixel aspect: 1/1, display aspect: 1.78");
                return new ProcessOutcome { ExitCode = 0 };
            }

            int now = Interlocked.Increment(ref _running);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                lock (Presets)
                {
                    Presets.Add(Arg(args, "--preset"));
                    EncodeArgs.Add(args.ToArray());
                }
                var output = Arg(args, "-o");
                int code = ExitCodeFor?.Invoke(args) ?? 0;
                if (code != 0)
                {
                    onLine?.Invoke("[12:00:00] hb_init: starting");
                    onLine?.Invoke("[12:00:01] libhb: scan thread found 0 valid title(s)");
                    File.WriteAllText(output, "partial");
                    return new ProcessOutcome { ExitCode = code };
                }
                onLine?.Invoke("Encoding: task 1 of 1, 10.00 % (30 fps, avg 30 fps, ETA 00h01m00s)");
                await Task.Delay(20);
                onLine?.Invoke("Encoding: task 1 of 1, 100.00 % (30 fps, avg 30 fps, ETA 00h00m00s)");
                File.WriteAllText(output, "encoded");
                return new ProcessOutcome { ExitCode = 0 };
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        });

        #endregion
    }

    #endregion
}

public class HardwareEncoderTests
{
    #region Fields

    private const string Help = @"Video Options:
   -e, --encoder <string>  Select video encoder:
                               svt_av1
                               svt_av1_10bit
                               x264
                               x264_10bit
                               nvenc_h264
                               x265
                               x265_10bit
                               nvenc_h265
                               nvenc_h265_10bit
                               qsv_h265
                               mpeg4";

    #endregion

    #region Methods

    /// <summary>Reads which graphics card encoders HandBrake can use.</summary>
    [Fact]
    public void Reads_which_graphics_card_encoders_HandBrake_can_use()
    {
        Assert.Equal(new[] { "nvenc", "qsv" }, HandBrake.ParseHardwareEncoders(Help));
        Assert.Empty(HandBrake.ParseHardwareEncoders("x264\nx265\nsvt_av1"));
        Assert.Equal(new[] { "vcn" }, HandBrake.ParseHardwareEncoders("vce_h264\nvce_h265"));
        Assert.Equal("NVIDIA NVENC", HandBrake.HardwareName(HandBrake.ParseHardwareEncoders(Help)));
    }

    /// <summary>Picks the preset for the resolution.</summary>
    [Theory]
    [InlineData(1080, 1920, "H.265 NVENC 1080p")]
    [InlineData(480, 720, "H.265 NVENC 1080p")]      //never upscales, so DVDs use it too
    [InlineData(2160, 3840, "H.265 NVENC 2160p 4K")]
    public void Picks_the_preset_for_the_resolution(int height, int width, string expected)
    {
        var pick = HandBrake.ChooseHardwarePreset(height, width, new[] { "nvenc", "qsv" }, Fixture.Catalog.Names.ToList());
        Assert.Equal(expected, pick!.Value.Preset);
    }

    /// <summary>AMD and Intel get their own presets.</summary>
    [Fact]
    public void AMD_and_Intel_get_their_own_presets()
    {
        Assert.Equal("H.265 VCN 1080p", HandBrake.ChooseHardwarePreset(1080, 1920, new[] { "vcn" }, Fixture.Catalog.Names.ToList())!.Value.Preset);
        Assert.Equal("H.265 QSV 1080p", HandBrake.ChooseHardwarePreset(1080, 1920, new[] { "qsv" }, Fixture.Catalog.Names.ToList())!.Value.Preset);
    }

    #endregion
}
