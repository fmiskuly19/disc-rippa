namespace DiscRippa.Tests;

/// <summary>A temporary folder that is deleted when the test ends.</summary>
public sealed class TempDir : IDisposable
{
    #region Properties

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erd-tests-" + Guid.NewGuid().ToString("N"));

    #endregion

    #region Constructors

    /// <summary>Creates the folder.</summary>
    public TempDir() => Directory.CreateDirectory(Path);

    #endregion

    #region Methods

    /// <summary>Writes a file in the folder and returns its path.</summary>
    public string File(string name, string content = "x")
    {
        var p = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    /// <summary>A path inside the folder.</summary>
    public string Combine(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>The names of the files in the folder, sorted.</summary>
    public string[] FileNames()
        => Directory.GetFiles(Path).Select(f => System.IO.Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Deletes the folder.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch
        {
        }
    }

    #endregion
}

/// <summary>Stands in for MakeMKV / HandBrake: each test supplies what the "program" prints and does.</summary>
public sealed class FakeRunner : IProcessRunner
{
    #region Fields

    public delegate Task<ProcessOutcome> Handler(string exe, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct);
    private readonly Handler _handler;

    #endregion

    #region Properties

    public List<string[]> Calls { get; } = new();

    /// <summary>The folder the last program was started in (null = the default).</summary>
    public string? WorkingDirectory { get; private set; }

    #endregion

    #region Constructors

    /// <summary>Creates a runner that answers with <paramref name="handler"/>.</summary>
    public FakeRunner(Handler handler) => _handler = handler;

    #endregion

    #region Methods

    /// <summary>Answers with the handler instead of running anything.</summary>
    public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
    {
        lock (Calls) Calls.Add(args.ToArray());
        return _handler(fileName, args, onLine, ct);
    }

    /// <summary>Answers with the handler instead of running anything.</summary>
    public Task<ProcessOutcome> RunInAsync(string? workingDirectory, string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
    {
        WorkingDirectory = workingDirectory;
        return RunAsync(fileName, args, onLine, ct);
    }

    /// <summary>A successful exit.</summary>
    public static Task<ProcessOutcome> Ok() => Task.FromResult(new ProcessOutcome { ExitCode = 0 });

    /// <summary>An exit with this code.</summary>
    public static Task<ProcessOutcome> Exit(int code) => Task.FromResult(new ProcessOutcome { ExitCode = code });

    /// <summary>Waits until cancelled, like a program that runs for a long time.</summary>
    public static async Task<ProcessOutcome> RunUntilCancelled(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
        }
        return new ProcessOutcome { ExitCode = -1, Cancelled = true };
    }

    #endregion
}

public static class Fixture
{
    #region Properties

    public static string PresetList => File.ReadAllText(
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "preset-list.txt"));

    public static PresetCatalog Catalog => PresetCatalog.Parse(PresetList);

    /// <summary>Real `cyanrip -I -d G:` output (cyanrip 0.9.3, The Eternal Dance disc 1).</summary>
    public static string[] CyanripInfo => Read("cyanrip-info.txt").Split('\n');

    /// <summary>Real output of ripping track 4, split into lines the way .NET reads them (\r and \n both end a line).</summary>
    public static string[] CyanripRip => Read("cyanrip-rip-track4.txt").Replace("\r\n", "\n").Split('\r', '\n');

    #endregion

    #region Methods

    /// <summary>The text of a file in Fixtures.</summary>
    private static string Read(string name) => File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    #endregion
}
