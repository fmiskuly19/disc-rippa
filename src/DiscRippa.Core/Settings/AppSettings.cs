using System.Globalization;
using System.Text.Json;
using System.Text;

namespace DiscRippa.Core.Settings;

public sealed class AppSettings
{
    #region Properties

    public string? MakeMkvPath { get; set; }
    public string? HandBrakePath { get; set; }

    /// <summary>"Keep Ripping!": after each disc, eject it and wait for the next one.</summary>
    public bool AutoNext { get; set; } = true;

    /// <summary>Automation mode: a disc that goes in is read, ripped (main movie), queued for compression and ejected with no clicks.</summary>
    public bool AutomationMode { get; set; }

    public bool Notifications { get; set; } = true;

    /// <summary>"System" (follow Windows' light / dark setting), "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public string DefaultPreset { get; set; } = Presets.Auto;

    /// <summary>Keep the ripped MKV after its compressed copy is done. Off: the MKV is deleted, leaving just the smaller file.</summary>
    public bool KeepMkv { get; set; } = true;

    /// <summary>Auto compression uses the graphics card when HandBrake can (5-10x faster, light on the CPU).</summary>
    public bool UseGraphicsCard { get; set; } = true;

    /// <summary>The MP4 / MKV choice last made on the disc screen, offered again for the next disc (and used by automation).</summary>
    public bool CompressToMkv { get; set; }

    /// <summary>Where ripped movies go (the "Output path" in Settings). Null = the last folder used, else Videos.</summary>
    public string? MovieFolder { get; set; }

    /// <summary>Where the smaller (compressed) copies go (the "Compressed output path" in Settings). Null = next to the ripped MKV.</summary>
    public string? CompressedFolder { get; set; }

    /// <summary>The folder rips went to before there was an Output path setting; still the starting point for "Add files…".</summary>
    public string? LastFolder { get; set; }

    /// <summary>"Video" (DVD / Blu-ray) or "Cd" (music CD): which side of the app is showing.</summary>
    public string Mode { get; set; } = "Video";

    public string? CyanripPath { get; set; }

    /// <summary>Where music goes (Artist\Album\01 - Title.flac inside it). Null = the Music folder.</summary>
    public string? MusicFolder { get; set; }

    public bool CdAutoNext { get; set; } = true;
    public bool CdAutomation { get; set; }

    /// <summary>Look the album up on MusicBrainz (names, cover art).</summary>
    public bool CdLookup { get; set; } = true;

    /// <summary>The CD drive's read offset in samples (cyanrip insists on it). Null = use the known value for the drive, else 0.</summary>
    public int? CdOffset { get; set; }

    /// <summary>Simulate MakeMKV and HandBrake so the workflow can be tried without a disc.</summary>
    public bool DebugMode { get; set; }

    #endregion

    #region Methods

    /// <summary>Reads the settings file, or returns the defaults if there isn't one.</summary>
    public static AppSettings Load(string path) => JsonFile.Load<AppSettings>(path) ?? new AppSettings();

    /// <summary>Writes the settings file.</summary>
    public void Save(string path) => JsonFile.Save(path, this);

    #endregion
}
