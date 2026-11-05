using Microsoft.Win32.SafeHandles;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;

namespace DiscRippa.Core.Music;

/// <summary>Album details for a CD, from MusicBrainz via cyanrip (or typed in by the user).</summary>
public sealed class CdMetadata
{
    #region Properties

    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Date { get; set; } = "";
    public Dictionary<int, string> TrackTitles { get; } = new();
    public Dictionary<int, string> TrackArtists { get; } = new();

    /// <summary>Which disc of a box set this is (1 when it's a single CD).</summary>
    public int Disc { get; set; } = 1;

    public int TotalDiscs { get; set; } = 1;
    public string DiscName { get; set; } = "";

    /// <summary>"HL-DT-ST BD-RE BP50NB40   1.01" (from cyanrip's "Device model:" line).</summary>
    public string DriveModel { get; set; } = "";

    public bool Found => Album.Length > 0 || TrackTitles.Count > 0;

    /// <summary>At least one track has a real name.</summary>
    public bool HasTrackNames => TrackTitles.Values.Any(t => t.Trim().Length > 0);

    /// <summary>Where the names came from: the disc itself (CD-Text), MusicBrainz, or nowhere.</summary>
    public MetadataSource Source { get; set; } = MetadataSource.None;

    #endregion
}

public enum MetadataSource { None, CdText, MusicBrainz }
