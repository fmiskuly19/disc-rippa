using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace DiscRippa.Core.Simulation;

/// <summary>
/// Debug mode: pretends to be MakeMKV and HandBrakeCLI so the whole workflow can be tried in a couple of
/// minutes without a disc. It answers the same commands with the same kind of output the real programs
/// print, writes small placeholder files instead of video, and honours Abort / Stop.
///
/// Discs cycle through three kinds so every path gets exercised:
///   1. a clean movie disc (main movie + extras)
///   2. a scratched disc whose rip reports read errors (tests the warning flow)
///   3. a DVD of TV episodes (tests "rip every title" and the 480p auto preset)
/// </summary>
public sealed class SimulatedRunner : IProcessRunner
{
    #region Fields

    public const string FakeMakeMkv = "makemkvcon (simulated)";
    public const string FakeHandBrake = "HandBrakeCLI (simulated)";
    public const string FakeDevice = "SIM:";
    private static readonly Regex SizeRegex = new(@"size=(\d+)x(\d+)", RegexOptions.Compiled);
    private readonly object _gate = new();
    private int _discNumber = 1;
    private bool _discPresent;
    private DateTime? _insertAt;
    private DateTime? _removeAt;
    private bool _trayOpen;

    /// <summary>The first words of every file debug mode writes.</summary>
    public const string PlaceholderMarker = "DiscRippa DEBUG MODE placeholder";

    /// <summary>A shortened copy of HandBrake 1.11's own list, in the same format, so parsing is exercised.</summary>
    private const string PresetListText =
@"General/
    Fast 2160p60 4K HEVC
        H.265 video (up to 2160p60) and AAC stereo audio, in an MP4
        container.
    Fast 1080p30
        H.264 video (up to 1080p30) and AAC stereo audio, in an MP4
        container.
    Fast 720p30
        H.264 video (up to 720p30) and AAC stereo audio, in an MP4
        container.
    Fast 576p25
        H.264 video (up to 576p25) and AAC stereo audio, in an MP4
        container.
    Fast 480p30
        H.264 video (up to 480p30) and AAC stereo audio, in an MP4
        container.
    HQ 2160p60 4K HEVC Surround
        High quality H.265 video (up to 2160p60), AAC stereo audio,
        and Dolby Digital (AC-3) surround audio, in an MP4
        container.
    HQ 1080p30 Surround
        High quality H.264 video (up to 1080p30), AAC stereo audio,
        and Dolby Digital (AC-3) surround audio, in an MP4
        container.
    HQ 720p30 Surround
        High quality H.264 video (up to 720p30), AAC stereo audio,
        and Dolby Digital (AC-3) surround audio, in an MP4
        container.
    HQ 576p25 Surround
        High quality H.264 video (up to 576p25), AAC stereo audio,
        and Dolby Digital (AC-3) surround audio, in an MP4
        container.
    HQ 480p30 Surround
        High quality H.264 video (up to 480p30), AAC stereo audio,
        and Dolby Digital (AC-3) surround audio, in an MP4
        container.
    Super HQ 1080p30 Surround
        Super high quality H.264 video (up to 1080p30), AAC stereo
        audio, and Dolby Digital (AC-3) surround audio, in an MP4
        container.
Matroska/
    H.265 MKV 2160p60 4K
        H.265 video (up to 2160p60) and AAC stereo audio, in an MKV
        container.
    H.265 MKV 1080p30
        H.265 video (up to 1080p30) and AAC stereo audio, in an MKV
        container.
    H.265 MKV 480p30
        H.265 video (up to 480p30) and AAC stereo audio, in an MKV
        container.
CLI Defaults/
    CLI Default

HandBrake has exited.
";

    #endregion

    #region Properties

    /// <summary>1.0 = normal speed (a main movie takes about 15 seconds). Tests use something tiny.</summary>
    public double SpeedFactor { get; set; } = 1.0;

    /// <summary>How long after the tray empties the next disc "goes in".</summary>
    public TimeSpan DiscSwapDelay { get; set; } = TimeSpan.FromSeconds(3);

    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private SimDisc CurrentDisc
    {
        get
        {
            int n = _discNumber;
            switch ((n - 1) % 3)
            {
                case 0:
                    return new SimDisc($"SIMULATED_MOVIE_{n}", $"Simulated Movie {n}", "Blu-ray disc", "1920x1080", false, new[]
                    {
                        new SimTitle(0, "1:52:10", 25_300_000_000, 28, "00800.mpls"),
                        new SimTitle(1, "0:22:30", 4_100_000_000, 4, "00801.mpls"),
                        new SimTitle(2, "0:02:05", 310_000_000, 1, "00010.m2ts"),
                        new SimTitle(3, "0:00:45", 90_000_000, 1, "00011.m2ts"),
                    });
                case 1:
                    return new SimDisc($"SCRATCHED_DISC_{n}", $"Scratched Disc {n}", "Blu-ray disc", "1920x1080", true, new[]
                    {
                        new SimTitle(0, "1:38:44", 22_000_000_000, 20, "00001.mpls"),
                        new SimTitle(1, "0:02:30", 380_000_000, 1, "00002.mpls"),
                    });
                default:
                    return new SimDisc($"TV_SEASON_{n}", $"TV Season {n}", "DVD disc", "720x480", false, new[]
                    {
                        new SimTitle(0, "0:44:12", 2_100_000_000, 6, "VTS_01_1.VOB"),
                        new SimTitle(1, "0:43:58", 2_080_000_000, 6, "VTS_02_1.VOB"),
                        new SimTitle(2, "0:44:40", 2_130_000_000, 6, "VTS_03_1.VOB"),
                        new SimTitle(3, "0:44:05", 2_090_000_000, 6, "VTS_04_1.VOB"),
                        new SimTitle(4, "0:01:10", 40_000_000, 1, "VTS_05_1.VOB"),
                    });
            }
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Deletes the placeholder files earlier debug sessions left in <paramref name="folder"/> (and its
    /// subfolders). Only files that start with the placeholder text are touched; anything else is left alone.
    /// </summary>
    public static int CleanUpPlaceholders(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        int removed = 0;
        var markers = new[] { PlaceholderMarker, "Easy Disc Ripper DEBUG MODE placeholder" };
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 64 * 1024) continue;           //placeholders are tiny; real videos never match
                string head;
                using (var reader = new StreamReader(path)) head = new string(ReadUpTo(reader, 256));
                //placeholders start with the marker; simulated FLAC files and cue sheets carry it near the top instead.
                if (markers.Any(m => head.Contains(m, StringComparison.Ordinal)))
                {
                    File.Delete(path);
                    removed++;
                }
            }
            catch
            {
            }
        }
        return removed;
    }

    /// <summary>Reads up to <paramref name="count"/> characters.</summary>
    private static char[] ReadUpTo(StreamReader reader, int count)
    {
        var buffer = new char[count];
        int read = reader.Read(buffer, 0, count);
        return buffer.Take(read).ToArray();
    }

    /// <summary>"Opens the tray": the disc leaves now and the next one goes in shortly after.</summary>
    public bool Eject()
    {
        lock (_gate)
        {
            if (_discPresent) TakeDiscOut();
            _trayOpen = true;
            return true;
        }
    }

    /// <summary>
    /// A rip finished without ejecting. Pretend the user takes the disc out a few seconds later, so
    /// "wait for the next disc" can be tested without a real tray.
    /// </summary>
    public void DiscFinished()
    {
        lock (_gate)
        {
            if (_discPresent) _removeAt = Clock() + DiscSwapDelay;
        }
    }

    /// <summary>Takes the pretend disc out.</summary>
    private void TakeDiscOut()
    {
        _discPresent = false;
        _removeAt = null;
        _discNumber++;
        _insertAt = Clock() + DiscSwapDelay;
    }

    /// <summary>Pretends to be MakeMKV or HandBrakeCLI.</summary>
    public async Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
    {
        //same delivery as the real ProcessRunner: lines reach the caller on its own (UI) thread, in batches.
        //the simulated work runs on background threads, and WPF only allows list changes on the UI thread.
        var batcher = new LineBatcher(SynchronizationContext.Current, onLine ?? (_ =>
        {
        }), TimeSpan.FromMilliseconds(100));
        try
        {
            return await RunSimulatedAsync(args, line =>
            {
                if (onLine != null) batcher.Add(line);
            }, ct).ConfigureAwait(true);
        }
        finally
        {
            batcher.Drain();   //back on the caller's thread: deliver anything still waiting
        }
    }

    /// <summary>Answers a MakeMKV or HandBrakeCLI command line the way they would.</summary>
    private async Task<ProcessOutcome> RunSimulatedAsync(IReadOnlyList<string> args, Action<string> say, CancellationToken ct)
    {
        void Say(string line) => say(line);

        try
        {
            if (args.Contains("--preset-list"))
            {
                Say(PresetListText);
                return Ok();
            }
            if (args.Contains("info"))
            {
                if (args.Contains("disc:9999")) return DriveList(Say);
                return await TitleListAsync(Say, ct).ConfigureAwait(false);
            }
            if (args.Contains("mkv")) return await RipAsync(args, Say, ct).ConfigureAwait(false);
            if (args.Contains("-t")) return Scan(args, Say);
            if (args.Contains("-o")) return await EncodeAsync(args, Say, ct).ConfigureAwait(false);
            return new ProcessOutcome { ExitCode = 1, Error = "The simulator doesn't know that command." };
        }
        catch (OperationCanceledException)
        {
            return new ProcessOutcome { ExitCode = -1, Cancelled = true };
        }
    }

    /// <summary>A successful exit.</summary>
    private static ProcessOutcome Ok() => new() { ExitCode = 0 };

    /// <summary>Waits, sped up by the speed factor.</summary>
    private Task Wait(double milliseconds, CancellationToken ct)
        => Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds * SpeedFactor)), ct);

    /// <summary>MakeMKV's drive list, with the pretend drive in it.</summary>
    private ProcessOutcome DriveList(Action<string> say)
    {
        string? label = null;
        bool trayOpen;
        lock (_gate)
        {
            var now = Clock();
            if (!_discPresent && _insertAt == null) _insertAt = now + DiscSwapDelay;   //first look: disc goes in shortly
            if (_discPresent && _removeAt != null && now >= _removeAt) TakeDiscOut();
            if (!_discPresent && _insertAt != null && now >= _insertAt)
            {
                _discPresent = true;
                _insertAt = null;
                _trayOpen = false;
            }
            if (_discPresent) label = CurrentDisc.Label;
            trayOpen = _trayOpen;
        }

        say("MSG:1005,0,1,\"MakeMKV v1.17.7 (simulated) started\",\"%1 started\",\"MakeMKV\"");
        say(label == null
            ? $"DRV:0,{(trayOpen ? 1 : 0)},999,0,\"BD-RE SIMULATED DRIVE\",\"\",\"{FakeDevice}\""
            : $"DRV:0,2,999,12,\"BD-RE SIMULATED DRIVE\",\"{label}\",\"{FakeDevice}\"");
        say("DRV:1,256,999,0,\"\",\"\",\"\"");
        return Ok();
    }

    /// <summary>MakeMKV's title list for the pretend disc.</summary>
    private async Task<ProcessOutcome> TitleListAsync(Action<string> say, CancellationToken ct)
    {
        SimDisc disc;
        lock (_gate) disc = CurrentDisc;

        await Wait(1500, ct).ConfigureAwait(false);   //reading a disc takes a moment
        say($"MSG:5085,0,0,\"Loaded content hash table, will verify integrity of M2TS files.\",\"\"");
        say($"CINFO:1,6209,\"{disc.Type}\"");
        say($"CINFO:2,0,\"{disc.Name}\"");
        say($"CINFO:32,0,\"{disc.Label}\"");
        say($"TCOUNT:{disc.Titles.Length}");
        bool dvd = disc.Type.StartsWith("DVD", StringComparison.Ordinal);
        foreach (var t in disc.Titles)
        {
            say($"TINFO:{t.Id},2,0,\"{disc.Name}\"");
            say($"TINFO:{t.Id},8,0,\"{t.Chapters}\"");
            say($"TINFO:{t.Id},9,0,\"{t.Duration}\"");
            say($"TINFO:{t.Id},10,0,\"{t.Size / 1_000_000_000.0:0.0} GB\"");
            say($"TINFO:{t.Id},11,0,\"{t.Size.ToString(CultureInfo.InvariantCulture)}\"");
            say($"TINFO:{t.Id},16,0,\"{t.Source}\"");
            say($"TINFO:{t.Id},27,0,\"{disc.Label}_t{t.Id:00}.mkv\"");

            int s = 0;
            //video
            say($"SINFO:{t.Id},{s},1,6201,\"Video\"");
            say($"SINFO:{t.Id},{s},6,0,\"{(dvd ? "Mpeg2" : "Mpeg4")}\"");
            say($"SINFO:{t.Id},{s},7,0,\"{(dvd ? "Mpeg2" : "Mpeg4 AVC High@L4.1")}\"");
            say($"SINFO:{t.Id},{s},19,0,\"{disc.Size}\"");
            say($"SINFO:{t.Id},{s},20,0,\"{(dvd ? "4:3" : "16:9")}\"");
            say($"SINFO:{t.Id},{s},21,0,\"{(dvd ? "29.97 (30000/1001)" : "23.976 (24000/1001)")}\"");
            s++;
            //audio
            var audio = dvd
                ? new[] { ("eng", "English", "AC3", "Dolby Digital", 2, "Stereo") }
                : new[] { ("eng", "English", "TrueHD", "Dolby TrueHD", 8, "7.1"),
                          ("eng", "English", "AC3", "Dolby Digital", 6, "5.1(side)"),
                          ("fra", "French", "DTS", "DTS", 6, "5.1(side)") };
            foreach (var (code, lang, shortName, longName, ch, layout) in audio)
            {
                say($"SINFO:{t.Id},{s},1,6202,\"Audio\"");
                say($"SINFO:{t.Id},{s},3,0,\"{code}\"");
                say($"SINFO:{t.Id},{s},4,0,\"{lang}\"");
                say($"SINFO:{t.Id},{s},6,0,\"{shortName}\"");
                say($"SINFO:{t.Id},{s},7,0,\"{longName}\"");
                say($"SINFO:{t.Id},{s},14,0,\"{ch}\"");
                say($"SINFO:{t.Id},{s},40,0,\"{layout}\"");
                s++;
            }
            //subtitles
            foreach (var (code, lang) in dvd ? new[] { ("eng", "English") } : new[] { ("eng", "English"), ("fra", "French"), ("spa", "Spanish") })
            {
                say($"SINFO:{t.Id},{s},1,6203,\"Subtitles\"");
                say($"SINFO:{t.Id},{s},3,0,\"{code}\"");
                say($"SINFO:{t.Id},{s},4,0,\"{lang}\"");
                say($"SINFO:{t.Id},{s},6,0,\"{(dvd ? "Dvd Subtitles" : "PGS")}\"");
                say($"SINFO:{t.Id},{s},7,0,\"{(dvd ? "Dvd Subtitles" : "HDMV PGS Subtitles")}\"");
                s++;
            }
        }
        return Ok();
    }

    /// <summary>Pretends to rip titles, writing small placeholder files.</summary>
    private async Task<ProcessOutcome> RipAsync(IReadOnlyList<string> args, Action<string> say, CancellationToken ct)
    {
        //-r --progress=-same mkv disc:N <title> <dest>
        int titleId = int.Parse(args[args.Count - 2], CultureInfo.InvariantCulture);
        string dest = args[args.Count - 1];

        SimDisc disc;
        lock (_gate) disc = CurrentDisc;
        var title = disc.Titles.FirstOrDefault(t => t.Id == titleId) ?? disc.Titles[0];
        int seconds = ParseSeconds(title.Duration);

        say($"MSG:5014,0,2,\"Saving 1 titles into directory file://{dest.Replace('\\', '/')}\",\"\"");
        var file = Path.Combine(dest, $"title_t{titleId:00}.mkv");
        File.WriteAllText(file,
            $"DiscRippa DEBUG MODE placeholder. This is not a real video.\n" +
            $"Simulated rip of {disc.Label}, title {titleId} ({title.Duration}).\nsize={disc.Size}\n");

        //like the real MakeMKV: first a stage that opens and scans the disc (its own 0-100%),
        //then the saving stage. Only the saving stage should move the progress bar.
        say("PRGT:5018,0,\"Opening disc\"");
        say("PRGC:5018,0,\"Processing title sets\"");
        for (int i = 1; i <= 10; i++)
        {
            await Wait(150, ct).ConfigureAwait(false);
            int value = 65536 * i / 10;
            say($"PRGV:{value},{value},65536");
        }

        say("PRGT:5017,0,\"Saving to MKV file\"");
        say("PRGC:5018,0,\"Analyzing seamless segments\"");
        for (int i = 1; i <= 5; i++)
        {
            await Wait(120, ct).ConfigureAwait(false);
            say($"PRGV:{65536 * i / 5},0,65536");
        }
        say("PRGC:5017,0,\"Saving to MKV file\"");

        //a main movie takes about 15 seconds; a trailer a few.
        double totalMs = 3000 + seconds * 1.8;
        const int steps = 60;
        bool isMain = title == disc.Titles[0];
        for (int i = 1; i <= steps; i++)
        {
            await Wait(totalMs / steps, ct).ConfigureAwait(false);
            int value = (int)(65536L * i / steps);
            say($"PRGV:{value},{value},65536");
            if (disc.Scratched && isMain && (i == steps / 3 || i == steps * 2 / 3))
            {
                say($"MSG:2003,0,3,\"Error 'Scsi error - MEDIUM ERROR:UNRECOVERED READ ERROR' occurred while reading " +
                    $"'/BDMV/STREAM/00800.m2ts' at offset '{1048576L * i}'\",\"\"");
            }
        }

        say("MSG:5036,0,1,\"Copy complete. 1 titles saved.\",\"%1 titles saved\",\"1\"");
        return Ok();
    }

    /// <summary>HandBrake's scan output for a file.</summary>
    private ProcessOutcome Scan(IReadOnlyList<string> args, Action<string> say)
    {
        var source = ArgAfter(args, "-i");
        string size = "1920x1080";
        try
        {
            //only the first few KB: real video files might be added to the queue too.
            using var stream = File.OpenRead(source);
            var buffer = new byte[4096];
            int read = stream.Read(buffer, 0, buffer.Length);
            var m = SizeRegex.Match(Encoding.UTF8.GetString(buffer, 0, read));
            if (m.Success) size = $"{m.Groups[1].Value}x{m.Groups[2].Value}";
        }
        catch
        {
        }

        say("[12:00:00] hb_init: starting libhb thread (simulated)");
        say("+ title 1:");
        say($"  + size: {size}, pixel aspect: 1/1, display aspect: 1.78, 23.976 fps");
        return Ok();
    }

    /// <summary>Pretends to compress a file with HandBrake.</summary>
    private async Task<ProcessOutcome> EncodeAsync(IReadOnlyList<string> args, Action<string> say, CancellationToken ct)
    {
        var source = ArgAfter(args, "-i");
        var output = ArgAfter(args, "-o");
        var preset = ArgAfter(args, "--preset");

        File.WriteAllText(output,
            $"DiscRippa DEBUG MODE placeholder. This is not a real video.\n" +
            $"Simulated HandBrake encode of {Path.GetFileName(source)} with preset '{preset}'.\n");

        const int steps = 40;
        const double totalMs = 10_000;
        for (int i = 1; i <= steps; i++)
        {
            await Wait(totalMs / steps, ct).ConfigureAwait(false);
            double pct = 100.0 * i / steps;
            int left = (int)Math.Round((steps - i) * totalMs / steps / 1000);
            say(string.Format(CultureInfo.InvariantCulture,
                "Encoding: task 1 of 1, {0:0.00} % (120.00 fps, avg 118.50 fps, ETA 00h00m{1:00}s)", pct, left));
        }
        say("Encode done!");
        return Ok();
    }

    /// <summary>The value after a command-line flag, or "".</summary>
    private static string ArgAfter(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count - 1; i++)
            if (args[i] == name) return args[i + 1];
        return "";
    }

    /// <summary>"1:52:10" as seconds.</summary>
    private static int ParseSeconds(string duration)
    {
        var parts = duration.Split(':').Select(p => int.TryParse(p, out var n) ? n : 0).ToList();
        while (parts.Count < 3) parts.Insert(0, 0);
        return parts[0] * 3600 + parts[1] * 60 + parts[2];
    }

    #endregion

    #region Nested types

    private sealed record SimTitle(int Id, string Duration, long Size, int Chapters, string Source);
    private sealed record SimDisc(string Label, string Name, string Type, string Size, bool Scratched, SimTitle[] Titles);

    #endregion
}
