using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DiscRippa.Core.Processes;

/// <summary>Runs an external program. Faked in the tests.</summary>
public interface IProcessRunner
{
    #region Methods

    /// <summary>
    /// Runs the program and reports every line it prints (stdout and stderr) to <paramref name="onLine"/>.
    /// If <paramref name="ct"/> is cancelled the program is killed and the outcome says so.
    /// When called from a UI thread, <paramref name="onLine"/> is called on that same thread.
    /// </summary>
    Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct);

    /// <summary>The same, started in <paramref name="workingDirectory"/> (cyanrip names its folders relative to it).</summary>
    Task<ProcessOutcome> RunInAsync(string? workingDirectory, string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
        => RunAsync(fileName, args, onLine, ct);

    #endregion
}
