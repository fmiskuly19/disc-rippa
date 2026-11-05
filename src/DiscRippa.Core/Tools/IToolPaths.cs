namespace DiscRippa.Core.Tools;

/// <summary>Where MakeMKV, HandBrakeCLI and cyanrip are on this PC (null = not found).</summary>
public interface IToolPaths
{
    #region Properties

    string? MakeMkv { get; }
    string? HandBrake { get; }
    string? Cyanrip { get; }

    #endregion

    #region Events

    event Action? Changed;

    #endregion

    #region Methods

    /// <summary>Looks for the programs again (after a path was changed in Settings, say).</summary>
    void Refresh();

    #endregion
}

public sealed class ToolPaths : IToolPaths
{
    #region Fields

    private readonly ISettingsService _settings;
    private readonly RunMode _mode;

    #endregion

    #region Properties

    public string? MakeMkv { get; private set; }
    public string? HandBrake { get; private set; }
    public string? Cyanrip { get; private set; }

    #endregion

    #region Constructors

    /// <summary>Finds the programs now (debug mode uses the simulators instead).</summary>
    public ToolPaths(ISettingsService settings, RunMode mode)
    {
        _settings = settings;
        _mode = mode;
        Refresh();
    }

    #endregion

    #region Events

    public event Action? Changed;

    #endregion

    #region Methods

    /// <summary>Looks for the programs again and raises <see cref="Changed"/>.</summary>
    public void Refresh()
    {
        if (_mode.IsDebug)
        {
            //the simulators stand in for all three programs, installed or not.
            MakeMkv = SimulatedRunner.FakeMakeMkv;
            HandBrake = SimulatedRunner.FakeHandBrake;
            Cyanrip = SimulatedCd.FakeCyanrip;
        }
        else
        {
            var s = _settings.Current;
            MakeMkv = ToolLocator.FindMakeMkv(s.MakeMkvPath);
            HandBrake = ToolLocator.FindHandBrake(s.HandBrakePath);
            Cyanrip = ToolLocator.FindCyanrip(s.CyanripPath);
        }
        Changed?.Invoke();
    }

    #endregion
}
