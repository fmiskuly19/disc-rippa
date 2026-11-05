using System.Collections.ObjectModel;

namespace DiscRippa.Core.Music;

/// <summary>Waits for a music CD to go into a drive.</summary>
public sealed class CdWatcher
{
    #region Fields

    private readonly ICdDriveProbe _probe;
    private readonly TimeSpan _pollInterval;

    #endregion

    #region Properties

    /// <summary>The CD we just finished (its <see cref="CdDriveState.Signature"/>). Ignored until it leaves the drive.</summary>
    public string? Ignore { get; set; }

    #endregion

    #region Constructors

    /// <summary>Creates a watcher that looks at the drives every <paramref name="pollInterval"/>.</summary>
    public CdWatcher(ICdDriveProbe probe, TimeSpan pollInterval)
    {
        _probe = probe;
        _pollInterval = pollInterval;
    }

    #endregion

    #region Events

    public event Action<IReadOnlyList<CdDriveState>>? DrivesChanged;

    #endregion

    #region Methods

    /// <summary>One look at the drives. Runs off the UI thread because Windows can take a few seconds per drive.</summary>
    public async Task<IReadOnlyList<CdDriveState>> CheckDrivesAsync(CancellationToken ct)
    {
        var drives = await Task.Run(() => _probe.GetDrives(), ct).ConfigureAwait(true);
        if (Ignore != null && drives.Any(d => d.Signature != Ignore && Ignore.StartsWith(d.Letter + "|", StringComparison.Ordinal)))
            Ignore = null;   //the finished CD has left its drive
        DrivesChanged?.Invoke(drives);
        return drives;
    }

    /// <summary>Completes with the first music CD found that isn't the one we just finished.</summary>
    public async Task<CdDriveState> WaitForCdAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var drives = await CheckDrivesAsync(ct).ConfigureAwait(true);
            var cd = drives.FirstOrDefault(d => d.IsReady && d.IsAudioCd && d.Signature != Ignore);
            if (cd != null) return cd;
            await Task.Delay(_pollInterval, ct).ConfigureAwait(true);
        }
    }

    #endregion
}
