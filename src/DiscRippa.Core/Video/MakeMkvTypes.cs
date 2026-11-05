using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace DiscRippa.Core.Video;

public sealed record DiscDrive(int Index, string DiscName, string Device, string DriveName = "");

/// <summary>One disc drive and what it is doing, from a MakeMKV "DRV:" line.</summary>
public sealed record DriveStatus(int Index, int State, string DriveName, string DiscName, string Device)
{
    #region Fields

    public const int EmptyClosed = 0, EmptyOpen = 1, Inserted = 2, Loading = 3;

    #endregion

    #region Properties

    /// <summary>"BD-RE HL-DT-ST WH16NS40 (D:)"</summary>
    public string Title => Device.Length > 0 ? $"{DriveName} ({Device})" : DriveName;

    public string StateText => State switch
    {
        EmptyClosed => "No disc in the drive",
        EmptyOpen => "Tray is open",
        Inserted => DiscName.Length > 0 ? $"Disc inserted: {DiscName}" : "Disc inserted",
        Loading => "Loading the disc…",
        _ => "Not ready",
    };

    #endregion
}

/// <summary>Disc-level information (MakeMKV "CINFO" lines).</summary>
public sealed class DiscInfo
{
    #region Properties

    public string Type { get; set; } = "";        //"Blu-ray disc", "DVD disc"
    public string Name { get; set; } = "";        //e.g. "The Movie"
    public string VolumeName { get; set; } = "";  //the raw disc label

    #endregion
}

/// <summary>One video, audio or subtitle track inside a title (MakeMKV "SINFO" lines).</summary>
public sealed class StreamInfo
{
    #region Properties

    public int Index { get; set; }
    public string Type { get; set; } = "";          //"Video", "Audio", "Subtitles"
    public string CodecShort { get; set; } = "";
    public string CodecLong { get; set; } = "";
    public string LangCode { get; set; } = "";
    public string LangName { get; set; } = "";
    public string VideoSize { get; set; } = "";
    public string AspectRatio { get; set; } = "";
    public string FrameRate { get; set; } = "";
    public int Channels { get; set; }
    public string ChannelLayout { get; set; } = "";
    public string Description { get; set; } = "";

    #endregion
}

public sealed class TitleInfo
{
    #region Properties

    public int Id { get; set; }
    public int Seconds { get; set; }
    public string Duration { get; set; } = "";
    public long Size { get; set; }
    public string Name { get; set; } = "";
    public int Chapters { get; set; }
    public string SizeText { get; set; } = "";
    public string SourceFile { get; set; } = "";      //e.g. "00800.mpls"
    public string OutputFileName { get; set; } = "";
    public List<StreamInfo> Streams { get; set; } = new();

    #endregion
}

/// <summary>What a "PRGT"/"PRGC" line says MakeMKV is doing now.</summary>
public sealed record ProgressTitle(bool IsTotal, string Name);
