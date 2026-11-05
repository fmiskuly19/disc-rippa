using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscRippa.Core.Drives;

public static class CdaReader
{
    #region Methods

    /// <summary>Reads every TrackNN.cda in a CD's root folder. Empty for data/video discs.</summary>
    public static List<CdTrack> ReadTracks(string root)
    {
        var tracks = new List<CdTrack>();
        string[] files;
        try
        {
            files = Directory.GetFiles(root, "*.cda");
        }
        catch
        {
            return tracks;
        }
        foreach (var file in files)
        {
            try
            {
                var track = Parse(File.ReadAllBytes(file));
                if (track != null) tracks.Add(track);
            }
            catch
            {
            }
        }
        return tracks.OrderBy(t => t.Number).ToList();
    }

    /// <summary>
    /// A .cda file is a 44-byte RIFF "CDDA" header: version (2), track number (2), disc serial (4),
    /// start sector (4), length in sectors (4), then start/length as MSF. 75 sectors = 1 second.
    /// </summary>
    public static CdTrack? Parse(byte[] data)
    {
        if (data.Length < 36) return null;
        if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') return null;
        if (data[8] != 'C' || data[9] != 'D' || data[10] != 'D' || data[11] != 'A') return null;
        int number = BitConverter.ToUInt16(data, 22);
        uint lengthSectors = BitConverter.ToUInt32(data, 32);
        if (number <= 0) return null;
        return new CdTrack { Number = number, Seconds = (int)Math.Round(lengthSectors / 75.0) };
    }

    #endregion
}
