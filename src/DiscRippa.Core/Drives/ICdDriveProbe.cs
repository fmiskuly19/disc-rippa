using System.Globalization;
using System.Text.RegularExpressions;

namespace DiscRippa.Core.Drives;

/// <summary>Looks at the CD drives. Faked in tests and debug mode.</summary>
public interface ICdDriveProbe
{
    #region Methods

    /// <summary>Every optical drive, with what is in it.</summary>
    IReadOnlyList<CdDriveState> GetDrives();

    /// <summary>Album and track names stored on the disc itself (CD-Text), or null if it has none.</summary>
    CdMetadata? ReadCdText(string letter);

    /// <summary>"HL-DT-ST BD-RE BP50NB40", or "" if unknown.</summary>
    string DriveModel(string letter);

    #endregion
}
