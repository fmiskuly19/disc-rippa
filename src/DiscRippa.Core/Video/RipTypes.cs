namespace DiscRippa.Core.Video;

/// <summary>
/// Where a rip has got to. <see cref="Percent"/> is the overall progress for the progress bar and only moves
/// while MakeMKV is actually saving (it first opens and scans the disc, which has its own 0-100%).
/// </summary>
public sealed record RipProgress(
    int TitleNumber,
    int TitleCount,
    string Duration,
    double Percent,
    string Stage = "",
    string Step = "",
    double StagePercent = 0,
    double StepPercent = 0,
    bool Saving = true);

public sealed class RipRequest
{
    #region Properties

    public int DriveIndex { get; set; }
    public IReadOnlyList<TitleInfo> Titles { get; set; } = Array.Empty<TitleInfo>();
    public string DestDir { get; set; } = "";
    public string BaseName { get; set; } = "";

    #endregion
}

public sealed class RipResult
{
    #region Properties

    /// <summary>File names (not paths) of the finished MKVs, inside the destination folder.</summary>
    public List<string> Files { get; } = new();

    /// <summary>MakeMKV messages that look like read problems (scratches, bad sectors...).</summary>
    public List<string> Problems { get; } = new();

    /// <summary>Every message MakeMKV printed.</summary>
    public List<string> Log { get; } = new();

    public bool Cancelled { get; set; }
    public string? Error { get; set; }
    public int RemovedPartialFiles { get; set; }
    public bool Success => !Cancelled && Error == null;

    #endregion
}
