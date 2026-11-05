using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace DiscRippa.Core.Processes;

public sealed class ProcessOutcome
{
    #region Properties

    public int ExitCode { get; init; }
    public bool Cancelled { get; init; }
    public bool StartFailed { get; init; }
    public string? Error { get; init; }

    #endregion
}
