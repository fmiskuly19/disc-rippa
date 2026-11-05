using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscRippa.Core.Drives;

/// <summary>
/// Windows shows every track of a music CD as a small "TrackNN.cda" file. Those files hold each track's exact
/// length, so a music CD can be recognised and its track list shown without any extra program.
/// </summary>
public sealed class WindowsCdDriveProbe : ICdDriveProbe
{
    #region Fields

    private readonly Dictionary<string, string> _models = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Methods

    /// <summary>Every optical drive Windows knows about, with what is in it.</summary>
    public IReadOnlyList<CdDriveState> GetDrives()
    {
        var result = new List<CdDriveState>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return result;
        }

        foreach (var d in drives.Where(d => d.DriveType == DriveType.CDRom))
        {
            var state = new CdDriveState { Letter = d.Name.TrimEnd('\\', '/') };
            try
            {
                state.IsReady = d.IsReady;
                if (state.IsReady)
                {
                    state.Label = SafeLabel(d);
                    state.Tracks = CdaReader.ReadTracks(d.RootDirectory.FullName);
                    state.IsAudioCd = state.Tracks.Count > 0;
                }
            }
            catch
            {
                state.IsReady = false;
            }
            result.Add(state);
        }
        return result;
    }

    /// <summary>The album and track names stored on the CD itself, or null if it has none.</summary>
    public CdMetadata? ReadCdText(string letter)
    {
        var packs = CdDriveNative.ReadCdTextPacks(letter);
        return packs == null ? null : CdText.Parse(packs);
    }

    /// <summary>The drive's make and model, e.g. "HL-DT-ST BD-RE BP50NB40", or "" if unknown.</summary>
    public string DriveModel(string letter)
    {
        lock (_models)
        {
            if (_models.TryGetValue(letter, out var known) && known.Length > 0) return known;
            var model = CdDriveNative.DriveModel(letter);
            _models[letter] = model;
            return model;
        }
    }

    /// <summary>The disc's label, or "" if Windows can't read it.</summary>
    private static string SafeLabel(DriveInfo d)
    {
        try
        {
            return d.VolumeLabel;
        }
        catch
        {
            return "";
        }
    }

    #endregion
}
