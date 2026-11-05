namespace DiscRippa.Core.Common;

/// <summary>Writes unexpected errors to crash.log in the app's data folder (Settings → "Open the app's data folder").</summary>
public static class CrashLog
{
    #region Methods

    /// <summary>Appends an exception to the crash log. Never throws.</summary>
    public static void Write(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            File.AppendAllText(AppPaths.CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            //nowhere left to report to.
        }
    }

    #endregion
}
