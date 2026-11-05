using System.Runtime.InteropServices;
using System.Text;

namespace DiscRippa.Core.Tools;

public static class KnownFolders
{
    #region Methods

    /// <summary>Win32 (shell32): the path of a known folder such as Downloads.</summary>
    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>The real Downloads folder, even if the user moved it to another drive. Null off Windows.</summary>
    public static string? Downloads()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var ptr) != 0) return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
