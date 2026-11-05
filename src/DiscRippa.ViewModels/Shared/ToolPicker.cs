namespace DiscRippa.ViewModels.Shared;

/// <summary>The "Locate…" buttons: pick MakeMKV, HandBrakeCLI or cyanrip by hand when they weren't found.</summary>
public sealed class ToolPicker
{
    #region Fields

    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly IToolPaths _tools;

    #endregion

    #region Constructors

    /// <summary>Creates the picker for the "Locate…" buttons.</summary>
    public ToolPicker(IDialogService dialogs, ISettingsService settings, IToolPaths tools)
    {
        _dialogs = dialogs;
        _settings = settings;
        _tools = tools;
    }

    #endregion

    #region Methods

    /// <summary>True if a MakeMKV was picked (and saved).</summary>
    public bool LocateMakeMkv()
    {
        var path = Pick("Select makemkvcon64.exe", "MakeMKV console (makemkvcon*.exe)|makemkvcon*.exe|All files|*.*", "makemkvcon",
                        "That doesn't look like MakeMKV's console program. Pick makemkvcon64.exe (in the MakeMKV install folder).");
        if (path == null) return false;
        _settings.Current.MakeMkvPath = path;
        return Save();
    }

    /// <summary>Lets the user pick HandBrakeCLI.exe. True if one was picked and saved.</summary>
    public bool LocateHandBrake()
    {
        var path = Pick("Select HandBrakeCLI.exe", "HandBrakeCLI|HandBrakeCLI*.exe|All files|*.*", "handbrakecli",
                        "That doesn't look like HandBrakeCLI. Pick the file named HandBrakeCLI.exe (not the regular HandBrake app).");
        if (path == null) return false;
        _settings.Current.HandBrakePath = path;
        return Save();
    }

    /// <summary>Lets the user pick cyanrip. True if one was picked and saved.</summary>
    public bool LocateCyanrip()
    {
        var path = Pick("Select cyanrip", "cyanrip|cyanrip*.exe|All files|*.*", "cyanrip",
                        "That doesn't look like cyanrip. Pick the cyanrip .exe you downloaded (e.g. cyanrip-win64.exe).");
        if (path == null) return false;
        _settings.Current.CyanripPath = path;
        return Save();
    }

    /// <summary>Saves the settings and looks for the programs again.</summary>
    private bool Save()
    {
        _settings.Save();
        _tools.Refresh();
        return true;
    }

    /// <summary>Lets the user pick a file, and checks its name looks right.</summary>
    private string? Pick(string title, string filter, string mustContain, string wrongFileMessage)
    {
        var path = _dialogs.PickFiles(title, filter)?.FirstOrDefault();
        if (path == null) return null;
        if (!Path.GetFileName(path).ToLowerInvariant().Contains(mustContain))
        {
            _dialogs.Show(wrongFileMessage, "Wrong file?", DialogIcon.Warning);
            return null;
        }
        return path;
    }

    #endregion
}
