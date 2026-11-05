using System.Diagnostics;
using DiscRippa.Views;

namespace DiscRippa.Services;

/// <summary>Explorer and the browser.</summary>
public sealed class ShellService : IShellService
{
    #region Methods

    /// <summary>Opens a folder in Explorer, with a file highlighted if it is there.</summary>
    public void OpenFolder(string folder, string? selectFile = null)
    {
        try
        {
            var file = selectFile == null ? null : Path.Combine(folder, selectFile);
            if (file != null && File.Exists(file))
                Process.Start("explorer.exe", $"/select,\"{file}\"");
            else
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialog.Show(null, ex.Message, "Couldn't open folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Opens a web page in the default browser.</summary>
    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    #endregion
}
