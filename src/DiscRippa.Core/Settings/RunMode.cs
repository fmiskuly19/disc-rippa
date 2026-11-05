namespace DiscRippa.Core.Settings;

/// <summary>
/// Whether discs are ripped for real or simulated (debug mode), and the files and folders that depend on it.
/// Fixed for the life of the app: switching debug mode restarts it.
/// </summary>
public sealed record RunMode(bool IsDebug)
{
    #region Properties

    /// <summary>Debug-mode rips go here, away from real movies.</summary>
    public static string VideoTestFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DiscRippa test");

    /// <summary>Debug-mode CD rips go here, away from real music.</summary>
    public static string MusicTestFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DiscRippa test");

    //debug mode keeps its history and queue in memory only (null = no file), so every debug session starts empty.
    public string? VideoHistoryFile => IsDebug ? null : AppPaths.HistoryFile;

    public string? MusicHistoryFile => IsDebug ? null : AppPaths.CdHistoryFile;
    public string? QueueFile => IsDebug ? null : AppPaths.QueueFile;
    public string LastRipLog => IsDebug ? AppPaths.DebugRipLog : AppPaths.LastRipLog;
    public string LastCdRipLog => IsDebug ? AppPaths.DebugCdRipLog : AppPaths.LastCdRipLog;

    /// <summary>How often the drives are looked at while waiting for a disc.</summary>
    public TimeSpan VideoPollInterval => TimeSpan.FromSeconds(IsDebug ? 1 : 4);

    public TimeSpan CdPollInterval => TimeSpan.FromSeconds(IsDebug ? 1 : 3);
    public TimeSpan IdlePollInterval => TimeSpan.FromSeconds(IsDebug ? 2 : 5);

    #endregion

    #region Methods

    /// <summary>Clears what earlier debug sessions left behind: their saved lists and placeholder files.</summary>
    public void ResetDebugData()
    {
        if (!IsDebug) return;
        FileNames.TryDelete(AppPaths.DebugHistoryFile);   //written by older versions
        FileNames.TryDelete(AppPaths.DebugQueueFile);
        try
        {
            SimulatedRunner.CleanUpPlaceholders(VideoTestFolder);
        }
        catch
        {
        }
        try
        {
            SimulatedRunner.CleanUpPlaceholders(MusicTestFolder);
        }
        catch
        {
        }
    }

    #endregion
}
