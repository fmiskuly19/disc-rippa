using System.Text;

namespace DiscRippa.Core.Music;

/// <summary>What a FLAC file's header says about it.</summary>
public sealed record FlacInfo(int SampleRate, int Channels, int BitsPerSample, long TotalSamples, long AudioBytes)
{
    #region Properties

    public double Seconds => SampleRate > 0 ? (double)TotalSamples / SampleRate : 0;

    #endregion
}

/// <summary>
/// Just enough of the FLAC format to check that a rip really produced audio, and to write the tags (names) ourselves,
/// so names with any characters (Björk, Lovin’, AC/DC) end up in the files exactly as shown on the CD screen.
/// </summary>
public static class Flac
{
    #region Fields

    private const byte StreamInfo = 0, Padding = 1, VorbisComment = 4;

    #endregion

    #region Methods

    /// <summary>The file's audio details, or null if it isn't a complete FLAC file (empty, truncated, not FLAC).</summary>
    public static FlacInfo? Inspect(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var blocks = ReadBlocks(f, out long audioStart);
            if (blocks == null || blocks.Count == 0 || blocks[0].Type != StreamInfo || blocks[0].Data.Length < 34) return null;
            var s = blocks[0].Data;
            int rate = (s[10] << 12) | (s[11] << 4) | (s[12] >> 4);
            int channels = ((s[12] >> 1) & 0x07) + 1;
            int bits = (((s[12] & 0x01) << 4) | (s[13] >> 4)) + 1;
            long total = ((long)(s[13] & 0x0F) << 32) | ((long)s[14] << 24) | ((long)s[15] << 16) | ((long)s[16] << 8) | s[17];
            long audioBytes = f.Length - audioStart;
            if (rate <= 0 || total <= 0 || audioBytes < 2) return null;

            //the audio must start with a FLAC frame (sync code 0xFFF8 / 0xFFF9).
            f.Position = audioStart;
            int b0 = f.ReadByte(), b1 = f.ReadByte();
            if (b0 != 0xFF || (b1 & 0xFE) != 0xF8) return null;
            return new FlacInfo(rate, channels, bits, total, audioBytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The tags in the file (upper-case keys), or empty.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ReadTags(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var blocks = ReadBlocks(f, out _);
            var vc = blocks?.FirstOrDefault(b => b.Type == VorbisComment);
            return vc == null ? Array.Empty<KeyValuePair<string, string>>() : ParseComments(vc.Data, out _);
        }
        catch
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }
    }

    /// <summary>
    /// Sets these tags (replacing any with the same names; empty values remove them). Other tags, such as cyanrip's
    /// ReplayGain and checksums, are kept. The file is rewritten through a temporary copy, so it is never left half-done.
    /// </summary>
    public static void WriteTags(string path, IReadOnlyDictionary<string, string> tags)
    {
        List<Block>? blocks;
        long audioStart;
        using (var f = File.OpenRead(path))
        {
            blocks = ReadBlocks(f, out audioStart);
            if (blocks == null || blocks.Count == 0 || blocks[0].Type != StreamInfo) throw new InvalidDataException($"{Path.GetFileName(path)} isn't a FLAC file.");
        }

        var vc = blocks.FirstOrDefault(b => b.Type == VorbisComment);
        string vendor = "DiscRippa";
        var comments = new List<KeyValuePair<string, string>>();
        if (vc != null) comments.AddRange(ParseComments(vc.Data, out vendor));
        var replaced = new HashSet<string>(tags.Keys.Select(k => k.ToUpperInvariant()));
        comments.RemoveAll(c => replaced.Contains(c.Key));
        foreach (var (key, value) in tags)
            if (!string.IsNullOrWhiteSpace(value)) comments.Add(new(key.ToUpperInvariant(), value.Trim()));

        var kept = blocks.Where(b => b.Type != VorbisComment && b.Type != Padding).ToList();
        kept.Add(new Block(VorbisComment, BuildComments(vendor, comments)));
        kept.Add(new Block(Padding, new byte[4096]));   //room for later tag edits without a rewrite

        var tmp = path + ".tagging";
        try
        {
            using (var input = File.OpenRead(path))
            using (var output = File.Create(tmp))
            {
                output.Write("fLaC"u8);
                for (int i = 0; i < kept.Count; i++) WriteBlock(output, kept[i], last: i == kept.Count - 1);
                input.Position = audioStart;
                input.CopyTo(output);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            FileNames.TryDelete(tmp);
        }
    }

    /// <summary>
    /// A tiny but well-formed FLAC file (header, tags, a stand-in for the audio). Used by debug mode and the tests.
    /// </summary>
    public static byte[] Minimal(string vendor, int seconds, string filler)
    {
        using var ms = new MemoryStream();
        ms.Write("fLaC"u8);
        var si = new byte[34];
        si[0] = 0x10; si[1] = 0x00; si[2] = 0x10; si[3] = 0x00;          //block size 4096
        long total = 44100L * Math.Max(1, seconds);
        //44100 Hz, 2 channels, 16 bits: rate(20) channels-1(3) bps-1(5) total(36)
        si[10] = (byte)(44100 >> 12); si[11] = (byte)((44100 >> 4) & 0xFF); si[12] = (byte)(((44100 & 0x0F) << 4) | (1 << 1) | (15 >> 4));
        si[13] = (byte)(((15 & 0x0F) << 4) | (int)((total >> 32) & 0x0F));
        si[14] = (byte)(total >> 24); si[15] = (byte)(total >> 16); si[16] = (byte)(total >> 8); si[17] = (byte)total;
        WriteBlock(ms, new Block(StreamInfo, si), last: false);
        WriteBlock(ms, new Block(VorbisComment, BuildComments(vendor, new List<KeyValuePair<string, string>>())), last: true);
        ms.WriteByte(0xFF); ms.WriteByte(0xF8);
        ms.Write(Encoding.UTF8.GetBytes(filler));
        return ms.ToArray();
    }

    /// <summary>The metadata blocks at the start of the file, and where the audio starts; null if it isn't FLAC.</summary>
    private static List<Block>? ReadBlocks(Stream f, out long audioStart)
    {
        audioStart = 0;
        var magic = new byte[4];
        if (f.Read(magic, 0, 4) != 4 || magic[0] != 'f' || magic[1] != 'L' || magic[2] != 'a' || magic[3] != 'C') return null;
        var blocks = new List<Block>();
        var header = new byte[4];
        while (true)
        {
            if (f.Read(header, 0, 4) != 4) return null;
            bool last = (header[0] & 0x80) != 0;
            byte type = (byte)(header[0] & 0x7F);
            int length = (header[1] << 16) | (header[2] << 8) | header[3];
            var data = new byte[length];
            if (ReadFully(f, data) != length) return null;
            blocks.Add(new Block(type, data));
            if (last) break;
            if (blocks.Count > 1000) return null;
        }
        audioStart = f.Position;
        return blocks;
    }

    /// <summary>Reads until the buffer is full or the stream ends. Returns how many bytes were read.</summary>
    private static int ReadFully(Stream f, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = f.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
        }
        return read;
    }

    /// <summary>Writes one metadata block with its header.</summary>
    private static void WriteBlock(Stream s, Block b, bool last)
    {
        int len = b.Data.Length;
        s.WriteByte((byte)((last ? 0x80 : 0) | b.Type));
        s.WriteByte((byte)(len >> 16)); s.WriteByte((byte)(len >> 8)); s.WriteByte((byte)len);
        s.Write(b.Data);
    }

    /// <summary>The tags in a Vorbis comment block, and its vendor string.</summary>
    private static List<KeyValuePair<string, string>> ParseComments(byte[] d, out string vendor)
    {
        var list = new List<KeyValuePair<string, string>>();
        vendor = "";
        int p = 0;
        int Int()
        {
            var v = BitConverter.ToInt32(d, p);
            p += 4;
            return v;
        }
        if (d.Length < 8) return list;
        int vl = Int();
        if (vl < 0 || p + vl > d.Length) return list;
        vendor = Encoding.UTF8.GetString(d, p, vl); p += vl;
        if (p + 4 > d.Length) return list;
        int n = Int();
        for (int i = 0; i < n && p + 4 <= d.Length; i++)
        {
            int l = Int();
            if (l < 0 || p + l > d.Length) break;
            var text = Encoding.UTF8.GetString(d, p, l); p += l;
            int eq = text.IndexOf('=');
            if (eq > 0) list.Add(new(text.Substring(0, eq).ToUpperInvariant(), text.Substring(eq + 1)));
        }
        return list;
    }

    /// <summary>A Vorbis comment block holding these tags.</summary>
    private static byte[] BuildComments(string vendor, List<KeyValuePair<string, string>> comments)
    {
        using var ms = new MemoryStream();
        void Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            ms.Write(BitConverter.GetBytes(b.Length));
            ms.Write(b);
        }
        Str(vendor);
        ms.Write(BitConverter.GetBytes(comments.Count));
        foreach (var (k, v) in comments) Str($"{k}={v}");
        return ms.ToArray();
    }

    #endregion

    #region Nested types

    private sealed record Block(byte Type, byte[] Data);

    #endregion
}
