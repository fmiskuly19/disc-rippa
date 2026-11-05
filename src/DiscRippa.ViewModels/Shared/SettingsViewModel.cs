namespace DiscRippa.ViewModels.Shared;

/// <summary>
/// The Settings window. Everything is edited on a copy here and only written to the settings when Save is clicked
/// (and the checks pass). Debug mode is handled by the main window afterwards, since switching it restarts the app.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    #region Fields

    private readonly ISettingsService _settings;
    private readonly IToolPaths _tools;
    private readonly IMakeMkvKeyService _keys;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly INotificationService _notify;

    /// <summary>Setting names for <see cref="Highlight"/>.</summary>
    public const string MovieFolderSetting = "MovieFolder";

    private readonly string? _foundMakeMkv, _foundHandBrake, _foundCyanrip;

    #endregion

    #region Properties

    /// <summary>The setting to scroll to and flash when the window opens (e.g. from "Change…" on the disc screen), or null.</summary>
    public string? Highlight { get; set; }

    [ObservableProperty] private string makeMkvPath;
    [ObservableProperty] private string handBrakePath;
    [ObservableProperty] private string cyanripPath;
    public string MakeMkvFound { get; }
    public string HandBrakeFound { get; }
    public string CyanripFound { get; }

    /// <summary>Where ripped movies go. Several titles from one disc get a folder of their own inside it.</summary>
    [ObservableProperty] private string movieFolder;

    /// <summary>Where the smaller copies go. Empty = next to the ripped MKV.</summary>
    [ObservableProperty] private string compressedFolder;

    public ObservableCollection<string> PresetChoices { get; }
    [ObservableProperty] private string defaultPreset;
    [ObservableProperty] private bool autoNext;

    /// <summary>Delete the ripped MKV once its smaller copy is done (the opposite of the KeepMkv setting).</summary>
    [ObservableProperty] private bool deleteOriginal;

    [ObservableProperty] private bool useGraphicsCard;
    public string GraphicsCardText { get; }
    [ObservableProperty] private bool cdLookup;
    [ObservableProperty] private bool notifications;

    /// <summary>The theme picker: 0 = match Windows, 1 = light, 2 = dark.</summary>
    [ObservableProperty] private int themeIndex;
    [ObservableProperty] private bool debugMode;
    [ObservableProperty] private string keyStatus = "";
    [ObservableProperty] private bool isKeyEntryVisible;
    [ObservableProperty] private string typedKey = "";
    [ObservableProperty] private string getKeyText = "Activate Blu-ray (get the free beta key)";

    #endregion

    #region Constructors

    /// <summary>Creates the Settings window's view model from the current settings.</summary>
    public SettingsViewModel(ISettingsService settings, IToolPaths tools, PresetService presets, IMakeMkvKeyService keys,
                             IDialogService dialogs, IShellService shell, INotificationService notify)
    {
        _notify = notify;
        _settings = settings;
        _tools = tools;
        _keys = keys;
        _dialogs = dialogs;
        _shell = shell;

        var s = settings.Current;
        movieFolder = AppFolders.SavedMovieFolder(s);
        compressedFolder = AppFolders.SavedCompressedFolder(s) ?? "";
        //a program the app found by itself is shown too, so you can see which one is used.
        _foundMakeMkv = s.MakeMkvPath == null ? Existing(tools.MakeMkv) : null;
        _foundHandBrake = s.HandBrakePath == null ? Existing(tools.HandBrake) : null;
        _foundCyanrip = s.CyanripPath == null ? Existing(tools.Cyanrip) : null;
        makeMkvPath = s.MakeMkvPath ?? _foundMakeMkv ?? "";
        handBrakePath = s.HandBrakePath ?? _foundHandBrake ?? "";
        cyanripPath = s.CyanripPath ?? _foundCyanrip ?? "";
        cdLookup = s.CdLookup;
        autoNext = s.AutoNext;
        deleteOriginal = !s.KeepMkv;
        useGraphicsCard = s.UseGraphicsCard;
        notifications = s.Notifications;
        themeIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        debugMode = s.DebugMode;
        PresetChoices = presets.Choices;
        defaultPreset = presets.DefaultPreset;

        MakeMkvFound = FoundText(s.MakeMkvPath, tools.MakeMkv, "Not found. Install MakeMKV, or point to makemkvcon64.exe.");
        HandBrakeFound = FoundText(s.HandBrakePath, tools.HandBrake, "Not found. Only needed for the smaller copy.");
        CyanripFound = FoundText(s.CyanripPath, tools.Cyanrip, "Not found. Only needed for music CDs.");
        GraphicsCardText = tools.HandBrake == null ? "Needs HandBrakeCLI."
            : !presets.HasChecked ? "Still checking which graphics card HandBrake can use…"
            : presets.GraphicsCardName != null
                ? $"Found: {presets.GraphicsCardName}. Files come out a little bigger than the CPU's HQ presets at the same quality. " +
                  "If a graphics-card encode fails, that file is redone on the CPU automatically."
                : "HandBrake didn't find a graphics card it can use on this PC, so Auto uses the CPU.";
        ShowKeyStatus();
    }

    #endregion

    #region Events

    /// <summary>Raised when the window should close: true = saved, false = cancelled.</summary>
    public event Action<bool>? CloseRequested;

    #endregion

    #region Commands

    /// <summary>Fetches and installs the free MakeMKV beta key.</summary>
    [RelayCommand]
    private async Task GetBetaKeyAsync()
    {
        var old = GetKeyText;
        GetKeyText = "Getting the key…";
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var (key, until) = await _keys.FetchBetaKeyAsync(limit.Token);
            bool same = _keys.InstalledKey() == key;
            _keys.Install(key);
            ShowKeyStatus(same
                ? "You already had the current beta key, so nothing changed."
                : "Installed the current beta key from the MakeMKV forum. Blu-rays will work now" + (until.Length > 0 ? $" (it's valid until {until})." : "."));
        }
        catch (Exception ex)
        {
            ShowKeyStatus();
            if (_dialogs.Confirm($"Couldn't get the key automatically.\n\n{ex.Message}\n\nYou can open the forum page, copy the key " +
                                 "(it starts with \"T-\") and paste it with \"Enter a key myself…\". Open the page now?",
                                 "Activate Blu-ray", DialogIcon.Warning))
            {
                _shell.OpenUrl(MakeMkvKey.BetaKeyPage);
                IsKeyEntryVisible = true;
            }
        }
        finally
        {
            GetKeyText = old;
        }
    }

    /// <summary>Shows or hides the box for typing a key.</summary>
    [RelayCommand]
    private void ToggleKeyEntry() => IsKeyEntryVisible = !IsKeyEntryVisible;

    /// <summary>Installs the key typed in the box.</summary>
    [RelayCommand]
    private void UseTypedKey()
    {
        var key = TypedKey.Trim().Trim('"');
        if (!MakeMkvKey.IsValidKey(key))
        {
            _dialogs.Show("That doesn't look like a MakeMKV key. Keys start with \"T-\" (or another letter and a dash) and are about 60 characters long, with no spaces.",
                          "Check the key", DialogIcon.Warning);
            return;
        }
        try
        {
            _keys.Install(key);
            TypedKey = "";
            IsKeyEntryVisible = false;
            ShowKeyStatus("Key installed. Blu-rays will work now.");
        }
        catch (Exception ex)
        {
            _dialogs.Show($"Couldn't install the key:\n\n{ex.Message}", "Activate Blu-ray", DialogIcon.Error);
        }
    }

    /// <summary>Lets the user pick the output path.</summary>
    [RelayCommand]
    private void BrowseMovieFolder()
    {
        var current = MovieFolder.Trim().Trim('"');
        var picked = _dialogs.PickFolder("Where should ripped movies go?", Directory.Exists(current) ? current : null);
        if (picked != null) MovieFolder = picked;
    }

    /// <summary>Lets the user pick the compressed output path.</summary>
    [RelayCommand]
    private void BrowseCompressedFolder()
    {
        var current = CompressedFolder.Trim().Trim('"');
        var picked = _dialogs.PickFolder("Where should the smaller copies go?", Directory.Exists(current) ? current : null);
        if (picked != null) CompressedFolder = picked;
    }

    /// <summary>Lets the user pick makemkvcon64.exe.</summary>
    [RelayCommand]
    private void BrowseMakeMkv() => Browse(p => MakeMkvPath = p, MakeMkvPath, "Select makemkvcon64.exe",
                                           "MakeMKV console (makemkvcon*.exe)|makemkvcon*.exe|All files|*.*");

    /// <summary>Lets the user pick HandBrakeCLI.exe.</summary>
    [RelayCommand]
    private void BrowseHandBrake() => Browse(p => HandBrakePath = p, HandBrakePath, "Select HandBrakeCLI.exe",
                                             "HandBrakeCLI|HandBrakeCLI*.exe|All files|*.*");

    /// <summary>Lets the user pick cyanrip.</summary>
    [RelayCommand]
    private void BrowseCyanrip() => Browse(p => CyanripPath = p, CyanripPath, "Select cyanrip", "cyanrip|cyanrip*.exe|All files|*.*");

    /// <summary>Sends a test notification, and explains what to check if Windows refused it.</summary>
    [RelayCommand]
    private void TestNotification()
    {
        var problem = _notify.Test();
        if (problem != null)
            _dialogs.Show($"{problem}\n\nIf nothing appeared, check Windows Settings > System > Notifications: notifications must be on, " +
                          "Do not disturb off, and this app allowed in the list of apps.", "Test notification", DialogIcon.Warning);
    }

    /// <summary>Opens the app's data folder (logs, history, queue) in Explorer.</summary>
    [RelayCommand]
    private void OpenDataFolder() => _shell.OpenFolder(AppPaths.Dir);

    /// <summary>Closes the window without saving.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>Checks the entries, saves them and closes the window.</summary>
    [RelayCommand]
    private void Save()
    {
        var problem = Check();
        if (problem != null)
        {
            _dialogs.Show(problem.Value.Message, problem.Value.Title, DialogIcon.Warning);
            return;
        }

        var s = _settings.Current;
        s.MovieFolder = Clean(MovieFolder);
        s.CompressedFolder = Clean(CompressedFolder);
        s.MakeMkvPath = Chosen(MakeMkvPath, _foundMakeMkv);
        s.HandBrakePath = Chosen(HandBrakePath, _foundHandBrake);
        s.CyanripPath = Chosen(CyanripPath, _foundCyanrip);
        s.CdLookup = CdLookup;
        s.DefaultPreset = DefaultPreset ?? Presets.Auto;
        s.AutoNext = AutoNext;
        s.KeepMkv = !DeleteOriginal;
        s.UseGraphicsCard = UseGraphicsCard;
        s.Notifications = Notifications;
        s.Theme = ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        s.DebugMode = DebugMode;   //the main window restarts the app if this changed
        _settings.Save();
        _tools.Refresh();
        CloseRequested?.Invoke(true);
    }

    #endregion

    #region Methods

    /// <summary>Shows whether a MakeMKV key is installed, plus an optional extra line.</summary>
    private void ShowKeyStatus(string? extra = null)
    {
        var key = _keys.InstalledKey();
        KeyStatus = (key == null
            ? "No key installed yet. MakeMKV will refuse Blu-rays (it asks to start a trial, which it can't do from here)."
            : $"✓  A key is installed ({MakeMkvKey.Short(key)}). If Blu-rays stop working, the beta key has probably expired: activate again to get the new one.")
            + (extra != null ? "\n\n" + extra : "");
    }

    /// <summary>Lets the user pick a program file, starting where the current one is.</summary>
    private void Browse(Action<string> set, string current, string title, string filter)
    {
        current = current.Trim().Trim('"');
        var start = current.Length > 0 && File.Exists(current) ? Path.GetDirectoryName(current) : null;
        var picked = _dialogs.PickFiles(title, filter, start)?.FirstOrDefault();
        if (picked != null) set(picked);
    }

    /// <summary>What's wrong with the entries (title, message), or null if they're fine.</summary>
    private (string Title, string Message)? Check()
    {
        var mk = Clean(MakeMkvPath);
        var hb = Clean(HandBrakePath);
        var cr = Clean(CyanripPath);
        if (mk != null && !File.Exists(mk)) return ("Check the path", $"MakeMKV wasn't found at:\n{mk}");
        if (hb != null && !File.Exists(hb)) return ("Check the path", $"HandBrakeCLI wasn't found at:\n{hb}");
        if (hb != null && !Path.GetFileName(hb).ToLowerInvariant().Contains("handbrakecli"))
            return ("Wrong file?", "That doesn't look like HandBrakeCLI. Pick the file named HandBrakeCLI.exe (not the regular HandBrake app).");
        if (cr != null && !File.Exists(cr)) return ("Check the path", $"cyanrip wasn't found at:\n{cr}");
        var movies = Clean(MovieFolder);
        if (movies == null || !Path.IsPathRooted(movies))
            return ("Check the output path", "Pick a folder for ripped discs, like D:\\Movies (use Browse…).");
        var compressed = Clean(CompressedFolder);
        if (compressed != null && !Path.IsPathRooted(compressed))
            return ("Check the compressed output path", "Pick a folder for the smaller copies, like D:\\Movies (small) (use Browse…), or leave it empty to keep them next to the MKV.");
        return null;
    }

    /// <summary>The path if the file exists, else null.</summary>
    private static string? Existing(string? path) => path != null && File.Exists(path) ? path : null;

    /// <summary>The line under a program path: found automatically, missing, or nothing.</summary>
    private static string FoundText(string? chosen, string? used, string missing)
        => chosen != null ? (used != null ? "" : "That file doesn't exist any more.")
         : used != null ? "Found automatically." : missing;

    /// <summary>What to save: nothing (keep finding it automatically) if the box still shows the program the app found.</summary>
    private static string? Chosen(string text, string? found)
    {
        var path = Clean(text);
        return found != null && string.Equals(path, found, StringComparison.OrdinalIgnoreCase) ? null : path;
    }

    /// <summary>The text without spaces or quotes around it, or null if that leaves nothing.</summary>
    private static string? Clean(string text)
    {
        var t = text.Trim().Trim('"');
        return t.Length == 0 ? null : t;
    }

    #endregion
}
