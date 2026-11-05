using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace DiscRippa.Core.Drives;

/// <summary>Talks to the CD drive through Windows: its model name and the disc's CD-Text.</summary>
public static class CdDriveNative
{
    #region Fields

    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x3;
    private const uint OpenExisting = 3;
    private const uint IoctlCdromReadTocEx = 0x00024054;
    private const uint IoctlStorageQueryProperty = 0x002D1400;

    #endregion

    #region Methods

    /// <summary>Win32: opens a handle to a device.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    /// <summary>Win32: sends a control code to a device.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize,
                                               byte[] output, int outputSize, out int returned, IntPtr overlapped);

    /// <summary>Opens the CD drive with this letter for reading, or null if it can't be.</summary>
    private static SafeFileHandle? Open(string letter)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var l = letter.Trim().TrimEnd('\\', '/');
        if (l.Length != 2 || l[1] != ':') return null;
        var h = CreateFile($@"\\.\{l}", GenericRead, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            h.Dispose();
            return null;
        }
        return h;
    }

    /// <summary>The raw CD-Text packs on the disc in <paramref name="letter"/>, or null if it has none.</summary>
    public static byte[]? ReadCdTextPacks(string letter)
    {
        try
        {
            using var h = Open(letter);
            if (h == null) return null;
            //CDROM_READ_TOC_EX: Format = CDROM_READ_TOC_EX_FORMAT_CDTEXT (5), MSF 0, session 0.
            var input = new byte[] { 0x05, 0, 0, 0 };
            var output = new byte[65536];
            if (!DeviceIoControl(h, IoctlCdromReadTocEx, input, input.Length, output, output.Length, out int got, IntPtr.Zero)) return null;
            if (got < 4 + CdText.PackSize) return null;
            int length = (output[0] << 8) | output[1];            //bytes after the length field
            int packBytes = Math.Min(length - 2, got - 4);
            if (packBytes < CdText.PackSize) return null;
            return output.AsSpan(4, packBytes - packBytes % CdText.PackSize).ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"HL-DT-ST BD-RE BP50NB40" — the drive's vendor and model, or "" if Windows won't say.</summary>
    public static string DriveModel(string letter)
    {
        try
        {
            using var h = Open(letter);
            if (h == null) return "";
            var query = new byte[12];   //STORAGE_PROPERTY_QUERY: StorageDeviceProperty, PropertyStandardQuery
            var output = new byte[1024];
            if (!DeviceIoControl(h, IoctlStorageQueryProperty, query, query.Length, output, output.Length, out int got, IntPtr.Zero)) return "";
            string At(int offsetField)
            {
                int off = BitConverter.ToInt32(output, offsetField);
                if (off <= 0 || off >= got) return "";
                int end = off;
                while (end < got && output[end] != 0) end++;
                return Encoding.ASCII.GetString(output, off, end - off).Trim();
            }
            return string.Join(" ", new[] { At(12), At(16) }.Where(s => s.Length > 0));
        }
        catch
        {
            return "";
        }
    }

    #endregion
}
