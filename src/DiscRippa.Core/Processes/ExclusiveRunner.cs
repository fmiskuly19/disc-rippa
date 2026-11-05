using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DiscRippa.Core.Processes;

/// <summary>
/// Lets only one program run at a time through it. MakeMKV calls go through one of these, so the
/// background drive check can never fight a disc read or a rip for the drive.
/// </summary>
public sealed class ExclusiveRunner : IProcessRunner
{
    #region Fields

    private readonly IProcessRunner _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    #endregion

    #region Constructors

    /// <summary>Wraps a runner so that only one program runs through it at a time.</summary>
    public ExclusiveRunner(IProcessRunner inner) => _inner = inner;

    #endregion

    #region Methods

    /// <summary>Waits for any program already running, then runs this one.</summary>
    public async Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> args, Action<string>? onLine, CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return new ProcessOutcome { ExitCode = -1, Cancelled = true };   //gave up waiting for its turn
        }
        try
        {
            return await _inner.RunAsync(fileName, args, onLine, ct).ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    #endregion
}
