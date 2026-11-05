namespace DiscRippa.ViewModels.Common;

/// <summary>Where a rip tab is: nothing going on, waiting for a disc, reading it, showing its screen, or ripping it.</summary>
public enum RipState { Idle, Waiting, Scanning, Reviewing, Ripping }

public static class RipStates
{
    #region Methods

    /// <summary>A disc is being worked on: read, shown, or ripped. (Waiting for one isn't.)</summary>
    public static bool IsWorking(this RipState state) => state is RipState.Scanning or RipState.Reviewing or RipState.Ripping;

    #endregion
}
