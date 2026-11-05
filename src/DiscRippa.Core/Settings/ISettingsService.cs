namespace DiscRippa.Core.Settings;

/// <summary>The app's settings: loaded once when the app starts, saved whenever something changes.</summary>
public interface ISettingsService
{
    #region Properties

    AppSettings Current { get; }

    #endregion

    #region Events

    /// <summary>Raised after every save, so anything that mirrors a setting can catch up.</summary>
    event Action? Saved;

    #endregion

    #region Methods

    /// <summary>Writes the settings and raises <see cref="Saved"/>.</summary>
    void Save();

    #endregion
}

/// <summary>Settings kept in a JSON file (or only in memory when no file is given, as in the tests).</summary>
public sealed class SettingsService : ISettingsService
{
    #region Fields

    private readonly string? _file;

    #endregion

    #region Properties

    public AppSettings Current { get; }

    #endregion

    #region Constructors

    /// <summary>Loads the settings from <paramref name="file"/> (or keeps them in memory when it is null).</summary>
    public SettingsService(string? file)
    {
        _file = file;
        Current = file == null ? new AppSettings() : AppSettings.Load(file);
    }

    #endregion

    #region Events

    public event Action? Saved;

    #endregion

    #region Methods

    /// <summary>Writes the settings and raises <see cref="Saved"/>.</summary>
    public void Save()
    {
        if (_file != null) Current.Save(_file);
        Saved?.Invoke();
    }

    #endregion
}
