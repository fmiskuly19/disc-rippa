namespace DiscRippa.Core.Processes;

/// <summary>
/// How each outside program is run. In debug mode they're the simulators; otherwise real processes.
/// MakeMKV calls go through an <see cref="ExclusiveRunner"/>, one at a time, so nothing fights over the drive.
/// </summary>
public sealed class ToolRunners
{
    #region Properties

    public IProcessRunner MakeMkv { get; }
    public IProcessRunner HandBrake { get; }
    public IProcessRunner Cyanrip { get; }

    #endregion

    #region Constructors

    /// <summary>Sets up the runners for MakeMKV (one at a time), HandBrake and cyanrip.</summary>
    public ToolRunners(IProcessRunner videoRunner, IProcessRunner cdRunner)
    {
        MakeMkv = new ExclusiveRunner(videoRunner);
        HandBrake = videoRunner;
        Cyanrip = cdRunner;
    }

    #endregion
}
