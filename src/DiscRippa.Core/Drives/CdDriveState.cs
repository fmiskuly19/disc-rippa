using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscRippa.Core.Drives;

/// <summary>One track of a music CD.</summary>
public sealed class CdTrack
{
    #region Properties

    public int Number { get; set; }
    public int Seconds { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";

    public string Duration => Seconds >= 3600
        ? $"{Seconds / 3600}:{Seconds / 60 % 60:00}:{Seconds % 60:00}"
        : $"{Seconds / 60}:{Seconds % 60:00}";

    #endregion
}

/// <summary>A CD drive and what's in it.</summary>
public sealed class CdDriveState
{
    #region Properties

    public string Letter { get; set; } = "";       //"G:"
    public bool IsReady { get; set; }               //a disc is in and readable
    public bool IsAudioCd { get; set; }             //...and it's a music CD
    public string Label { get; set; } = "";
    public List<CdTrack> Tracks { get; set; } = new();
    public int TotalSeconds => Tracks.Sum(t => t.Seconds);

    /// <summary>Identifies this particular disc (so "Keep Ripping!" never picks the same CD up twice).</summary>
    public string Signature => !IsReady ? $"{Letter}|empty"
        : $"{Letter}|{(IsAudioCd ? "audio" : "data")}|{Label}|{Tracks.Count}|{TotalSeconds}";

    public string StateText => !IsReady ? "No disc in the drive"
        : !IsAudioCd ? "Not a music CD (it's a data or video disc)"
        : $"Music CD inserted ({Tracks.Count} {(Tracks.Count == 1 ? "track" : "tracks")}, {FormatLength(TotalSeconds)})";

    #endregion

    #region Methods

    /// <summary>"48:21" or "1:02:03".</summary>
    public static string FormatLength(int seconds) => seconds >= 3600
        ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
        : $"{seconds / 60}:{seconds % 60:00}";

    #endregion
}
