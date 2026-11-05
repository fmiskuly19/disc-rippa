namespace DiscRippa.ViewModels.Shared;

/// <summary>
/// Which page the main window shows: the normal view, or Settings in its place. Opening Settings returns a task that
/// completes when the user goes back (true = they saved).
/// </summary>
public sealed partial class Navigation : ObservableObject
{
    #region Fields

    private readonly Func<SettingsViewModel> _createSettings;
    private TaskCompletionSource<bool>? _settingsClosed;

    #endregion

    #region Properties

    /// <summary>The Settings page while it's showing; null on the normal view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsOpen))]
    private SettingsViewModel? settings;

    /// <summary>True while the Settings page is showing.</summary>
    public bool IsSettingsOpen => Settings != null;

    #endregion

    #region Events

    /// <summary>Raised when the user leaves Settings: true if they saved.</summary>
    public event Action<bool>? SettingsClosed;

    #endregion

    #region Constructors

    /// <summary>Creates the navigation; each visit to Settings gets a fresh view model from <paramref name="createSettings"/>.</summary>
    public Navigation(Func<SettingsViewModel> createSettings) => _createSettings = createSettings;

    #endregion

    #region Methods

    /// <summary>
    /// Shows the Settings page, scrolled to and highlighting <paramref name="highlight"/> (a <see cref="SettingsViewModel"/>
    /// setting name) if given. Completes when the user goes back: true if they saved.
    /// </summary>
    public Task<bool> OpenSettingsAsync(string? highlight = null)
    {
        if (Settings != null && _settingsClosed != null) return _settingsClosed.Task;

        var vm = _createSettings();
        vm.Highlight = highlight;
        var closed = new TaskCompletionSource<bool>();
        _settingsClosed = closed;
        vm.CloseRequested += saved =>
        {
            Settings = null;
            SettingsClosed?.Invoke(saved);
            closed.TrySetResult(saved);
        };
        Settings = vm;
        return closed.Task;
    }

    #endregion
}
