using System.Collections.ObjectModel;

namespace DiscRippa.Core.Music;

public sealed class CdRipRequest
{
    #region Properties

    public string Drive { get; init; } = "";

    /// <summary>The album folder the FLAC files go into (Music\Artist\Album).</summary>
    public string Folder { get; init; } = "";

    public IReadOnlyList<int> Tracks { get; init; } = Array.Empty<int>();
    public CdMetadata Metadata { get; init; } = new();

    /// <summary>How many tracks the whole CD has (for the "track 3 of 12" tag).</summary>
    public int TotalTracks { get; init; }

    /// <summary>The drive's read offset in samples (cyanrip -s).</summary>
    public int Offset { get; init; }

    #endregion
}

/// <summary>Where a CD rip is: track <see cref="TrackIndex"/> of <see cref="TrackCount"/>, overall percent.</summary>
public sealed record CdRipProgress(int TrackIndex, int TrackCount, int TrackNumber, double TrackPercent, double Percent);

public sealed class CdRipResult
{
    #region Properties

    public bool Success { get; init; }
    public bool Cancelled { get; init; }
    public string Error { get; init; } = "";

    /// <summary>FLAC files written, relative to the album folder.</summary>
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

    public int Problems { get; init; }

    #endregion
}
