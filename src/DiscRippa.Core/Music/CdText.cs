using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace DiscRippa.Core.Music;

/// <summary>
/// CD-Text: album and track names some CDs carry on the disc itself. They're stored as 18-byte "packs":
/// type (0x80 title, 0x81 performer, …), track number, sequence, block/char-position, 12 bytes of text, CRC.
/// Strings are NUL-separated and run across packs; track 0 is the album. A lone TAB means "same as the previous track".
/// </summary>
public static class CdText
{
    #region Fields

    public const int PackSize = 18;
    private const byte Title = 0x80;
    private const byte Performer = 0x81;
    private const byte SizeInfo = 0x8F;

    #endregion

    #region Methods

    /// <summary>Album and track names from raw CD-Text packs (block 0 only), or null if there are none.</summary>
    public static CdMetadata? Parse(ReadOnlySpan<byte> packs)
    {
        var titles = new Dictionary<int, string>();
        var performers = new Dictionary<int, string>();
        var encoding = Encoding.Latin1;

        //the size-info pack says which character set block 0 uses (0 = ISO-8859-1, 1 = ASCII, 0x80 = MS-JIS).
        for (int i = 0; i + PackSize <= packs.Length; i += PackSize)
        {
            var p = packs.Slice(i, PackSize);
            if (p[0] == SizeInfo && ((p[3] >> 4) & 0x07) == 0 && (p[1] & 0x7F) == 0)
            {
                if (p[4] == 0x80) return null;   //japanese double-byte text: not handled
                break;
            }
        }

        Collect(packs, Title, titles, encoding);
        Collect(packs, Performer, performers, encoding);
        if (titles.Count == 0 && performers.Count == 0) return null;

        var meta = new CdMetadata { Source = MetadataSource.CdText };
        if (titles.TryGetValue(0, out var album)) meta.Album = album;
        if (performers.TryGetValue(0, out var artist)) meta.AlbumArtist = artist;
        foreach (var (track, t) in titles.Where(kv => kv.Key > 0)) meta.TrackTitles[track] = t;
        foreach (var (track, a) in performers.Where(kv => kv.Key > 0)) meta.TrackArtists[track] = a;
        return meta;
    }

    /// <summary>
    /// Builds CD-Text packs the way a disc stores them (used by debug mode and the tests).
    /// <paramref name="titles"/>[0] is the album, then one per track; the same for performers.
    /// </summary>
    public static byte[] Encode(IReadOnlyList<string> titles, IReadOnlyList<string> performers)
    {
        var packs = new List<byte>();
        byte seq = 0;
        void Pack(byte type, IReadOnlyList<string> strings)
        {
            var bytes = new List<(byte Byte, int Track)>();
            for (int t = 0; t < strings.Count; t++)
            {
                foreach (var b in Encoding.Latin1.GetBytes(strings[t])) bytes.Add((b, t));
                bytes.Add((0, t));
            }
            for (int i = 0; i < bytes.Count; i += 12)
            {
                var p = new byte[PackSize];
                p[0] = type;
                p[1] = (byte)bytes[i].Track;
                p[2] = seq++;
                int charPos = 0;
                for (int j = i - 1; j >= 0 && bytes[j].Track == bytes[i].Track && bytes[j].Byte != 0; j--) charPos++;
                p[3] = (byte)Math.Min(15, charPos);
                for (int k = 0; k < 12 && i + k < bytes.Count; k++) p[4 + k] = bytes[i + k].Byte;
                packs.AddRange(p);
            }
        }
        Pack(Title, titles);
        Pack(Performer, performers);
        return packs.ToArray();
    }

    /// <summary>True if every byte is zero.</summary>
    private static bool AllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) if (b != 0) return false;
        return true;
    }

    /// <summary>Gathers the text of one pack type into track-number → text.</summary>
    private static void Collect(ReadOnlySpan<byte> packs, byte type, Dictionary<int, string> into, Encoding encoding)
    {
        var buffer = new List<byte>();
        int track = -1;
        for (int i = 0; i + PackSize <= packs.Length; i += PackSize)
        {
            var p = packs.Slice(i, PackSize);
            if (p[0] != type) continue;
            int block = (p[3] >> 4) & 0x07;
            bool doubleByte = (p[3] & 0x80) != 0;
            if (block != 0 || doubleByte) continue;

            if (buffer.Count == 0 || track < 0) track = p[1] & 0x7F;
            for (int k = 4; k < 16; k++)
            {
                byte b = p[k];
                if (b != 0)
                {
                    buffer.Add(b);
                    continue;
                }
                if (buffer.Count == 0 && AllZero(p.Slice(k, 16 - k))) break;   //padding after the last string
                var raw = encoding.GetString(buffer.ToArray());
                var text = raw.Trim();
                if (raw == "\t") text = into.TryGetValue(track - 1, out var previous) ? previous : "";
                if (text.Length > 0 && !into.ContainsKey(track)) into[track] = text;
                buffer.Clear();
                track++;
            }
        }
    }

    #endregion
}
