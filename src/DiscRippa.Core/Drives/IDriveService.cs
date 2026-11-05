namespace DiscRippa.Core.Drives;

/// <summary>The disc drive itself: opening the tray, and the name Windows gives it.</summary>
public interface IDriveService
{
    #region Events

    /// <summary>Raised (on the thread that created the service, the UI thread) when a drive's proper name arrives.</summary>
    event Action? DriveNamesChanged;

    #endregion

    #region Methods

    /// <summary>Opens the tray of the drive MakeMKV calls <paramref name="device"/> ("G:").</summary>
    bool EjectVideoDrive(string device);

    /// <summary>Opens the tray of the CD drive <paramref name="letter"/> ("G:").</summary>
    bool EjectCdDrive(string letter);

    /// <summary>
    /// "HL-DT-ST BD-RE BP50NB40 (G:)": the name Windows gives the drive, so both modes show the same one.
    /// Until Windows has answered (it's asked once, in the background) <paramref name="fallback"/> is returned.
    /// </summary>
    string DriveName(string letter, string fallback);

    #endregion
}

public sealed class DriveService : IDriveService
{
    #region Fields

    private readonly Simulators _simulators;
    private readonly ICdDriveProbe _probe;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _models = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Constructors

    /// <summary>Creates a new <see cref="DriveService"/>.</summary>
    public DriveService(Simulators simulators, ICdDriveProbe probe)
    {
        _simulators = simulators;
        _probe = probe;
    }

    #endregion

    #region Events

    public event Action? DriveNamesChanged;

    #endregion

    #region Methods

    /// <summary>Opens the tray of the drive MakeMKV calls <paramref name="device"/>.</summary>
    public bool EjectVideoDrive(string device) => _simulators.Video?.Eject() ?? DiscEjector.Eject(device);

    /// <summary>Opens the tray of the CD drive <paramref name="letter"/>.</summary>
    public bool EjectCdDrive(string letter) => _simulators.Cd?.Eject() ?? DiscEjector.Eject(letter);

    /// <summary>The name Windows gives the drive, or <paramref name="fallback"/> until it has answered.</summary>
    public string DriveName(string letter, string fallback)
    {
        if (_simulators.Video != null || string.IsNullOrWhiteSpace(letter)) return fallback;
        letter = letter.Trim().TrimEnd('\\', '/');
        if (_models.TryGetValue(letter, out var known)) return known.Length > 0 ? $"{known} ({letter})" : fallback;

        _models[letter] = "";
        Task.Run(() => _probe.DriveModel(letter)).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result.Length == 0) return;
            _models[letter] = t.Result;
            if (_ui != null) _ui.Post(_ => DriveNamesChanged?.Invoke(), null);
            else DriveNamesChanged?.Invoke();
        }, TaskScheduler.Default);
        return fallback;
    }

    #endregion
}
