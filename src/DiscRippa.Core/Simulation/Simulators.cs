namespace DiscRippa.Core.Simulation;

/// <summary>The pretend drives used in debug mode; both are null when ripping for real.</summary>
public sealed record Simulators(SimulatedRunner? Video, SimulatedCd? Cd)
{
    #region Properties

    public static Simulators None { get; } = new(null, null);

    #endregion
}
