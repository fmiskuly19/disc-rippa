using System.Collections.ObjectModel;

namespace DiscRippa.Core.Compression;

/// <summary>
/// HandBrake's presets and graphics-card encoders, as found on this PC. Fills the quality lists ("Auto" first)
/// and tells the compress queue what it can use.
/// </summary>
public sealed class PresetService
{
    #region Fields

    private readonly IProcessRunner _runner;
    private readonly IToolPaths _tools;
    private readonly ISettingsService _settings;
    private readonly CompressQueue _queue;

    #endregion

    #region Properties

    /// <summary>"Auto" followed by every HandBrake preset. Bound to the quality lists.</summary>
    public ObservableCollection<string> Choices { get; } = new();

    public PresetCatalog Catalog { get; private set; } = PresetCatalog.Empty;

    /// <summary>Graphics-card encoders HandBrake can use here (e.g. "nvenc"); empty if none or not checked yet.</summary>
    public IReadOnlyCollection<string> HardwareEncoders { get; private set; } = Array.Empty<string>();

    /// <summary>"NVIDIA NVENC", "Intel Quick Sync"… or null when HandBrake found no graphics-card encoder.</summary>
    public string? GraphicsCardName => HandBrake.HardwareName(HardwareEncoders);

    /// <summary>The preset to pre-select: the saved default if HandBrake still has it, otherwise Auto.</summary>
    public string DefaultPreset => Choices.Contains(_settings.Current.DefaultPreset) ? _settings.Current.DefaultPreset : Presets.Auto;

    /// <summary>True once HandBrake has been asked (so "no graphics card found" really means none was found).</summary>
    public bool HasChecked { get; private set; }

    #endregion

    #region Constructors

    /// <summary>Creates a new <see cref="PresetService"/>.</summary>
    public PresetService(ToolRunners runners, IToolPaths tools, ISettingsService settings, CompressQueue queue)
    {
        _runner = runners.HandBrake;
        _tools = tools;
        _settings = settings;
        _queue = queue;
        ResetChoices(Presets.Fallback);
    }

    #endregion

    #region Events

    /// <summary>Raised after HandBrake's real preset list has replaced the fallback one.</summary>
    public event Action? Loaded;

    #endregion

    #region Methods

    /// <summary>Asks HandBrake which presets and graphics-card encoders it has.</summary>
    public async Task LoadAsync()
    {
        var hb = _tools.HandBrake;
        if (hb == null) return;
        try
        {
            await LoadFromAsync(hb);
        }
        finally
        {
            HasChecked = true;
        }
    }

    /// <summary>Reads the presets and graphics-card encoders from the given HandBrakeCLI.</summary>
    private async Task LoadFromAsync(string hb)
    {

        var lines = await RunAsync(hb, "--preset-list");
        if (lines == null) return;

        //HandBrake's help lists only the encoders it can really use on this PC, graphics cards included.
        var help = await RunAsync(hb, "--help");
        if (help != null)
        {
            HardwareEncoders = HandBrake.ParseHardwareEncoders(string.Join("\n", help));
            _queue.HardwareEncoders = HardwareEncoders;
        }

        var catalog = PresetCatalog.Parse(string.Join("\n", lines));
        if (!catalog.HasPresets) return;

        Catalog = catalog;
        _queue.Catalog = catalog;
        ResetChoices(catalog.Names);
        Loaded?.Invoke();
        _queue.Pump();
    }

    /// <summary>Runs HandBrakeCLI with one argument and returns what it printed, or null if it failed.</summary>
    private async Task<List<string>?> RunAsync(string handbrake, string arg)
    {
        var lines = new List<string>();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await _runner.RunAsync(handbrake, new[] { arg }, lines.Add, limit.Token);
            return lines;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Brings the list up to date in place. Clearing it instead would make every quality list bound to it lose its
    /// selection (and write that empty selection back), which is how the Settings quality box ended up blank.
    /// </summary>
    private void ResetChoices(IEnumerable<string> names)
    {
        var wanted = new[] { Presets.Auto }.Concat(names).Distinct().ToList();
        for (int i = Choices.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Choices[i])) Choices.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < Choices.Count && Choices[i] == wanted[i]) continue;
            int at = Choices.IndexOf(wanted[i]);
            if (at >= 0) Choices.Move(at, i);
            else Choices.Insert(i, wanted[i]);
        }
    }

    #endregion
}
