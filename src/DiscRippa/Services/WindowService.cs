using System.Diagnostics;

namespace DiscRippa.Services;

/// <summary>Restarts the app.</summary>
public sealed class WindowService : IWindowService
{
    #region Fields

    public const string RestartArg = "--restarted";

    #endregion

    #region Methods

    /// <summary>Starts a fresh copy of the app and closes this one.</summary>
    public void Restart()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(RestartArg);
        Process.Start(psi);
        Application.Current.Shutdown();
    }

    #endregion
}
