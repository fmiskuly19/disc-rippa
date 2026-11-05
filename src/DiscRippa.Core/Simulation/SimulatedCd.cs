using System.Globalization;

namespace DiscRippa.Core.Simulation;

/// <summary>
/// Debug mode for music CDs: a pretend drive and a pretend cyanrip. CDs alternate between an album that is
/// found online, a disc of a box set, and an unknown CD-R (not found, so names must be typed in).
/// </summary>
public sealed class SimulatedCd : ICdDriveProbe, IProcessRunner
{
    #region Fields

    public const string FakeCyanrip = "cyanrip (simulated)";
    public const string FakeDrive = "S:";
    private readonly object _gate = new();
    private string? _cwd;
    private int _cdNumber = 1;
    private bool _present;
    private DateTime? _insertAt;
    private DateTime? _removeAt;

    #endregion

    #region Properties

    public double SpeedFactor { get; set; } = 1.0;
    public TimeSpan DiscSwapDelay { get; set; } = TimeSpan.FromSeconds(3);
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private SimAlbum Current => ((_cdNumber - 1) % 3) switch
    {
        0 => new SimAlbum("The Simulators", $"Pretend Album {_cdNumber}", "2024-05-17", 1, 1, true, new[]
        {
            ("Opening Track", 214), ("Second Song", 187), ("Interlude", 52), ("The Long One", 402), ("Closing Time", 245),
        }, HasCdText: true),
        1 => new SimAlbum("Debug Orchestra", "The Box Set", "1999-01-01", 2, 3, true, new[]
        {
            ("Overture", 301), ("Movement II", 268), ("Finale", 355),
        }),
        _ => new SimAlbum("", "", "", 1, 1, false, new[]
        {
            ("", 180), ("", 200), ("", 160), ("", 220),
        }),
    };

    /// <summary>Lookups that reached "MusicBrainz" (so tests can check the disc's own names were used instead).</summary>
    public int OnlineLookups { get; private set; }

    #endregion

    #region Methods

    /// <summary>The pretend CD drive and what is in it.</summary>
    public IReadOnlyList<CdDriveState> GetDrives()
    {
        lock (_gate)
        {
            var now = Clock();
            if (!_present && _insertAt == null) _insertAt = now + DiscSwapDelay;
            if (_present && _removeAt != null && now >= _removeAt) TakeOut();
            if (!_present && _insertAt != null && now >= _insertAt)
            {
                _present = true;
                _insertAt = null;
            }

            var state = new CdDriveState { Letter = FakeDrive, IsReady = _present };
            if (_present)
            {
                var a = Current;
                state.IsAudioCd = true;
                state.Label = "Audio CD";
                state.Tracks = a.Tracks.Select((t, i) => new CdTrack { Number = i + 1, Seconds = t.Seconds }).ToList();
            }
            return new[] { state };
        }
    }

    /// <summary>The pretend CD's stored names (some pretend CDs have none).</summary>
    public CdMetadata? ReadCdText(string letter)
    {
        SimAlbum a;
        lock (_gate)
        {
            if (!_present) return null;
            a = Current;
        }
        if (!a.HasCdText) return null;
        var titles = new List<string> { a.Album };
        titles.AddRange(a.Tracks.Select(t => t.Title));
        var performers = new List<string> { a.Artist };
        performers.AddRange(a.Tracks.Select(_ => "\t"));   //"same as before", as real discs often store it
        return CdText.Parse(CdText.Encode(titles, performers));
    }

    /// <summary>The pretend drive's name.</summary>
    public string DriveModel(string letter) => "SIMULATED CD DRIVE";

    /// <summary>Opens the pretend tray; a new pretend CD goes in a few seconds later.</summary>
    public bool Eject()
    {
        lock (_gate)
        {
            if (_present) TakeOut();
        }
        return true;
    }

    /// <summary>A rip finished without ejecting: pretend the CD is taken out a little later.</summary>
    public void CdFinished()
    {
        lock (_gate)
        {
            if (_present) _removeAt = Clock() + DiscSwapDelay;
        }
    }

    /// <summary>Takes the pretend CD out.</summary>
    private void TakeOut()
    {
        _present = false;
        _removeAt = null;
        _cdNumber++;
        _insertAt = Clock() + DiscSwapDelay;
    }

    /// <summary>Pretends to be cyanrip.</summary>
    public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
        => RunInAsync(null, fileName, args, onLine, ct);

    /// <summary>Pretends to be cyanrip, run in <paramref name="workingDirectory"/>.</summary>
    public async Task<ProcessOutcome> RunInAsync(string? workingDirectory, string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
    {
        _cwd = workingDirectory;
        var batcher = new LineBatcher(SynchronizationContext.Current, onLine ?? (_ =>
        {
        }), TimeSpan.FromMilliseconds(100));
        try
        {
            return await RunSimulatedAsync(args, l =>
            {
                if (onLine != null) batcher.Add(l);
            }, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return new ProcessOutcome { ExitCode = -1, Cancelled = true };
        }
        finally
        {
            batcher.Drain();
        }
    }

    /// <summary>Waits, sped up by the speed factor.</summary>
    private Task Wait(double ms, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, ms * SpeedFactor)), ct);

    /// <summary>Answers a cyanrip command line the way cyanrip would.</summary>
    private async Task<ProcessOutcome> RunSimulatedAsync(IReadOnlyList<string> args, Action<string> say, CancellationToken ct)
    {
        SimAlbum album;
        bool present;
        lock (_gate)
        {
            album = Current;
            present = _present;
        }

        say($"Checking {FakeDrive} for cdrom...");
        if (!present)
        {
            say("Unable to init cdio context");
            return new ProcessOutcome { ExitCode = 1 };
        }
        say("Opening drive...");
        await Wait(800, ct).ConfigureAwait(false);

        bool noLookup = args.Contains("-N");
        bool known = album.Known && !noLookup;
        if (!noLookup) OnlineLookups++;
        if (album.Known && !noLookup) say($"Found MusicBrainz release: {album.Album} - {album.Artist}");
        else if (!noLookup) say("No MusicBrainz release found for this disc!");

        if (args.Contains("-I"))
        {
            InfoLines(album, known, say);
            return new ProcessOutcome { ExitCode = 0 };
        }
        return await RipAsync(args, album, known, say, ct).ConfigureAwait(false);
    }

    /// <summary>The same layout real cyanrip 0.9.3 prints for -I.</summary>
    private static void InfoLines(SimAlbum a, bool known, Action<string> say)
    {
        say("cyanrip 0.9.3 (simulated)");
        say($"System device:  {FakeDrive}");
        say("Device model:   SIMULATED CD DRIVE   1.00");
        say($"Disc tracks:    {a.Tracks.Length}");
        if (known)
        {
            say($"Disc number:    {a.Disc}");
            say($"Total discs:    {a.TotalDiscs}");
            say($"Album:          {a.Album}");
            say($"Album artist:   {a.Artist}");
        }
        say("AccurateRip:    not found");
        say("");
        say("Tracks:");
        for (int i = 0; i < a.Tracks.Length; i++)
        {
            say($"Track {i + 1} info:");
            say("  Properties:");
            say($"    Duration:    00:{a.Tracks[i].Seconds / 60:00}:{a.Tracks[i].Seconds % 60:00}.000");
            say("  Metadata:");
            if (known)
            {
                say($"    title:        {a.Tracks[i].Title}");
                say($"    artist:       {a.Artist}");
            }
            say($"    track:        {i + 1}");
            if (known)
            {
                say($"    date:         {a.Date}");
                say($"    album:        {a.Album}");
                say($"    album_artist: {a.Artist}");
                say($"    totaldiscs:   {a.TotalDiscs}");
                say($"    disc:         {a.Disc}");
            }
            say("");
        }
    }

    /// <summary>Pretends to rip the pretend CD, writing small placeholder FLAC files.</summary>
    private async Task<ProcessOutcome> RipAsync(IReadOnlyList<string> args, SimAlbum album, bool known, Action<string> say, CancellationToken ct)
    {
        var dirArg = ArgAfter(args, "-D") ?? "cyanrip";
        if (dirArg.Contains('\\') || dirArg.Contains(':'))
        {
            say($"Couldn't open path \"{dirArg}\" for writing: No such file or directory!");   //what real cyanrip does
            return new ProcessOutcome { ExitCode = 1 };
        }
        string folder = Path.Combine(_cwd ?? Path.GetTempPath(), dirArg);
        var tracks = (ArgAfter(args, "-l") ?? string.Join(",", Enumerable.Range(1, album.Tracks.Length)))
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToList();
        //the real Windows cyanrip mangles anything but plain ASCII in its arguments (that's how a whole CD once came out
        //as empty files), so the simulator does the same. The app must never pass such arguments.
        bool garbled = args.Any(x => x.Any(c => c > 127 || c == '"'));
        var naming = ArgAfter(args, "-F") ?? "{track} - {title}";

        Directory.CreateDirectory(folder);
        foreach (var n in tracks)
        {
            var title = known && n <= album.Tracks.Length ? album.Tracks[n - 1].Title : "";
            var name = naming.Replace("{track}", n.ToString("00")).Replace("{title}", title);
            var file = Path.Combine(folder, FileNames.Safe(name) + ".flac");
            File.WriteAllBytes(file, Array.Empty<byte>());
            for (int p = 0; p <= 100; p += 5)
            {
                int left = (100 - p) / 5 / 10 + 1;
                say($"Ripping and encoding track {n}, progress - {p}.00%, ETA - {left}s");
                await Wait(100, ct).ConfigureAwait(false);
            }
            say("Flushing encoders...");
            int seconds = n <= album.Tracks.Length ? album.Tracks[n - 1].Seconds : 60;
            if (!garbled)
                File.WriteAllBytes(file, Flac.Minimal(SimulatedRunner.PlaceholderMarker + " (simulated cyanrip)", seconds, $"track {n}"));
            say($"Track {n} ripped and encoded successfully!");
        }
        var log = ArgAfter(args, "-L");
        if (log != null) File.WriteAllText(Path.Combine(folder, log + ".log"), SimulatedRunner.PlaceholderMarker + ". Simulated rip log.\n");
        var cue = ArgAfter(args, "-M");
        if (cue != null)
        {
            var lines = new List<string> { SimulatedRunner.PlaceholderMarker.Insert(0, "REM ") };
            foreach (var n in tracks)
            {
                lines.Add($"FILE \"{n:00}.flac\" WAVE");
                lines.Add($"  TRACK {n:00} AUDIO");
                lines.Add("    INDEX 01 00:00:00");
            }
            File.WriteAllLines(Path.Combine(folder, cue + ".cue"), lines);
        }
        say("Paranoia status counts:");
        say("  READ:          1351");
        say("Ripping errors: 0");
        say($"Ripping finished at {DateTime.Now:yyyy-MM-ddTHH:mm:ss}");
        return new ProcessOutcome { ExitCode = 0 };
    }

    /// <summary>The value after a command-line flag, or null.</summary>
    private static string? ArgAfter(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count - 1; i++) if (args[i] == flag) return args[i + 1];
        return null;
    }

    #endregion

    #region Nested types

    /// <param name="HasCdText">The names are on the disc itself (so nothing is looked up online).</param>
    /// <param name="Known">MusicBrainz knows this CD.</param>
    private sealed record SimAlbum(string Artist, string Album, string Date, int Disc, int TotalDiscs, bool Known, (string Title, int Seconds)[] Tracks,
                                   bool HasCdText = false);

    #endregion
}
