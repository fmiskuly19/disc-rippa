using System.Runtime.InteropServices;
using System.Text;

namespace DiscRippa.Core.Drives;

/// <summary>Opens the disc drive's tray.</summary>
public static class DiscEjector
{
    #region Methods

    /// <summary>Win32 (winmm): sends a command to the media control interface.</summary>
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    /// <summary>Opens the tray of the drive with this letter ("D:"). Returns false if it couldn't.</summary>
    public static bool Eject(string? device)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(device)) return false;
        var letter = device.Trim().TrimEnd('\\', '/');
        if (letter.Length != 2 || !char.IsLetter(letter[0]) || letter[1] != ':') return false;

        try
        {
            if (MciSendString($"open {letter} type cdaudio alias erdeject", null, 0, IntPtr.Zero) != 0) return false;
            bool ok = MciSendString("set erdeject door open", null, 0, IntPtr.Zero) == 0;
            MciSendString("close erdeject", null, 0, IntPtr.Zero);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
