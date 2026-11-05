using System.Globalization;
using System.Text.Json;
using System.Text;

namespace DiscRippa.Core.Settings;

public static class AppPaths
{
    #region Fields

    private static bool _migrated;

    #endregion

    #region Properties

    public static string Dir
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var d = Path.Combine(appData, "DiscRippa");
            if (!_migrated)
            {
                _migrated = true;
                MigrateFrom(Path.Combine(appData, "EasyDiscRipper"), d);
            }
            try
            {
                Directory.CreateDirectory(d);
            }
            catch
            {
            }
            return d;
        }
    }

    public static string SettingsFile => Path.Combine(Dir, "settings.json");
    public static string HistoryFile => Path.Combine(Dir, "history.json");
    public static string QueueFile => Path.Combine(Dir, "queue.json");
    public static string LastRipLog => Path.Combine(Dir, "last_rip.log");

    //debug mode keeps its own history, queue and log so test runs never mix with real ones.
    public static string DebugHistoryFile => Path.Combine(Dir, "history-debug.json");

    public static string DebugQueueFile => Path.Combine(Dir, "queue-debug.json");
    public static string DebugRipLog => Path.Combine(Dir, "last_rip-debug.log");
    public static string CdHistoryFile => Path.Combine(Dir, "history-cd.json");
    public static string LastCdRipLog => Path.Combine(Dir, "last_cd_rip.log");
    public static string DebugCdRipLog => Path.Combine(Dir, "last_cd_rip-debug.log");
    public static string CrashLog => Path.Combine(Dir, "crash.log");

    #endregion

    #region Methods

    /// <summary>The app used to be called Easy Disc Ripper: bring its settings, history and queue across once.</summary>
    public static void MigrateFrom(string oldDir, string newDir)
    {
        try
        {
            if (Directory.Exists(newDir) || !Directory.Exists(oldDir)) return;
            Directory.CreateDirectory(newDir);
            foreach (var file in Directory.GetFiles(oldDir))
                File.Copy(file, Path.Combine(newDir, Path.GetFileName(file)), false);
        }
        catch
        {
            //starting fresh is fine if the old files can't be copied.
        }
    }

    #endregion
}
