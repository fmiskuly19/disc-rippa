namespace DiscRippa.Core.Video;

public sealed record DiscFound(int DriveIndex, string DiscLabel, string Device, IReadOnlyList<TitleInfo> Titles,
                               string DiscType = "", string DiscName = "", string DriveName = "");

public sealed class DiscReadException : Exception
{
    #region Properties

    /// <summary>The disc that couldn't be read (so it can be ejected and skipped).</summary>
    public string Label { get; }

    public string Device { get; }

    #endregion

    #region Constructors

    /// <summary>An error reading a disc, with the disc's label and drive.</summary>
    public DiscReadException(string message, string label = "", string device = "") : base(message)
    {
        Label = label;
        Device = device;
    }

    #endregion
}
