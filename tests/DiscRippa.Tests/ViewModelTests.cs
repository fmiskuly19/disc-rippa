namespace DiscRippa.Tests;

/// <summary>Stands in for the app's dialogs: records what was shown and answers questions as told.</summary>
public sealed class FakeDialogs : IDialogService
{
    #region Properties

    public List<(string Title, string Message)> Shown { get; } = new();
    public bool Answer { get; set; } = true;
    public string? Folder { get; set; }
    public IReadOnlyList<string>? Files { get; set; }

    #endregion

    #region Methods

    /// <summary>Records the message.</summary>
    public void Show(string message, string title, DialogIcon icon = DialogIcon.Information) => Shown.Add((title, message));

    /// <summary>Records the question and answers with <see cref="Answer"/>.</summary>
    public bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question, bool defaultNo = false)
    {
        Shown.Add((title, message));
        return Answer;
    }

    /// <summary>Records the question and answers with <see cref="Answer"/>.</summary>
    public bool ConfirmOkCancel(string message, string title, DialogIcon icon = DialogIcon.Information) => Confirm(message, title);

    /// <summary>Returns <see cref="Folder"/>.</summary>
    public string? PickFolder(string title, string? startFolder = null) => Folder;

    /// <summary>Returns <see cref="Files"/>.</summary>
    public IReadOnlyList<string>? PickFiles(string title, string filter, string? startFolder = null, bool multiple = false) => Files;

    #endregion
}

public sealed class FakeNotifications : INotificationService
{
    #region Properties

    public List<string> Titles { get; } = new();
    public string Status { get; private set; } = "";

    #endregion

    #region Methods

    /// <summary>Records the title.</summary>
    public void Notify(string title, string message) => Titles.Add(title);

    /// <summary>Records the text.</summary>
    public void SetStatus(string text) => Status = text;

    /// <summary>Records a test notification.</summary>
    public string? Test()
    {
        Titles.Add("test");
        return null;
    }

    #endregion
}

public sealed class FakeShell : IShellService
{
    #region Properties

    public List<string> Opened { get; } = new();

    #endregion

    #region Methods

    /// <summary>Records the folder.</summary>
    public void OpenFolder(string folder, string? selectFile = null) => Opened.Add(folder);

    /// <summary>Records the address.</summary>
    public void OpenUrl(string url) => Opened.Add(url);

    #endregion
}

public sealed class FakeWindows : IWindowService
{
    #region Methods

    /// <summary>Does nothing.</summary>
    public void Restart()
    {
    }

    #endregion
}

public sealed class FakeKeys : IMakeMkvKeyService
{
    #region Properties

    public string? Key { get; set; }

    #endregion

    #region Methods

    /// <summary>Returns <see cref="Key"/>.</summary>
    public string? InstalledKey() => Key;

    /// <summary>Sets <see cref="Key"/>.</summary>
    public void Install(string key) => Key = key;

    /// <summary>Returns a made-up key.</summary>
    public Task<(string Key, string ValidUntil)> FetchBetaKeyAsync(CancellationToken ct) => Task.FromResult(("T-" + new string('a', 60), "end of the month"));

    #endregion
}

/// <summary>The app's pieces in debug mode (simulated drives and programs, nothing saved to disk), as the app builds them.</summary>
public sealed class DebugApp
{
    #region Fields

    private Navigation? _navigation;

    #endregion

    #region Properties

    public SettingsService Settings { get; }
    public RunMode Mode { get; }
    public Simulators Simulators { get; }
    public ToolRunners Runners { get; }
    public ToolPaths Tools { get; }
    public CompressQueue Queue { get; }
    public PresetService Presets { get; }
    public HistoryStores History { get; }
    public DriveService Drives { get; }
    public ToolPicker Picker { get; }
    public FakeDialogs Dialogs { get; } = new();
    public FakeNotifications Notifications { get; } = new();
    public FakeShell Shell { get; } = new();
    public FakeWindows Windows { get; } = new();

    /// <summary>The page navigation, opening this app's Settings view model.</summary>
    public Navigation Navigation => _navigation ??= new Navigation(() => SettingsScreen());

    #endregion

    #region Constructors

    /// <summary>Builds the app's pieces in debug mode.</summary>
    public DebugApp()
    {
        Settings = new SettingsService(null);
        Mode = new RunMode(true);
        Simulators = new Simulators(new SimulatedRunner(), new SimulatedCd());
        Runners = new ToolRunners(Simulators.Video!, Simulators.Cd!);
        Tools = new ToolPaths(Settings, Mode);
        Queue = new CompressQueue(Runners.HandBrake, null) { HandBrakePath = () => Tools.HandBrake };
        Presets = new PresetService(Runners, Tools, Settings, Queue);
        History = new HistoryStores(new HistoryStore(null), new HistoryStore(null, music: true));
        Drives = new DriveService(Simulators, Simulators.Cd!);
        Picker = new ToolPicker(Dialogs, Settings, Tools);
    }

    #endregion

    #region Methods

    /// <summary>A DVD / Blu-ray tab built from these pieces.</summary>
    public VideoRipViewModel Video() => new(
        new DiscWatcher(Runners.MakeMkv, () => Tools.MakeMkv, Mode.VideoPollInterval), Runners, Tools, Settings, Mode,
        new AppFolders(Mode, Settings), Presets, Queue, History, Drives, Simulators.Cd!, Simulators, Dialogs, Notifications, Picker, Navigation);

    /// <summary>A Music CD tab built from these pieces.</summary>
    public CdRipViewModel Cd() => new(
        new CdWatcher(Simulators.Cd!, Mode.CdPollInterval), Runners, Tools, Settings, Mode, new AppFolders(Mode, Settings),
        History, Drives, Simulators.Cd!, Simulators, Dialogs, Notifications, Picker);

    /// <summary>A Settings window view model built from these pieces.</summary>
    public SettingsViewModel SettingsScreen(FakeKeys? keys = null) => new(Settings, Tools, Presets, keys ?? new FakeKeys(), Dialogs, Shell, Notifications);

    #endregion
}

public class SettingsViewModelTests
{
    #region Methods

    /// <summary>Saving writes every setting and closes.</summary>
    [Fact]
    public void Saving_writes_every_setting_and_closes()
    {
        var app = new DebugApp();
        var vm = app.SettingsScreen();
        bool? closed = null;
        vm.CloseRequested += saved => closed = saved;
        int saves = 0;
        app.Settings.Saved += () => saves++;

        vm.DeleteOriginal = true;
        vm.CdLookup = false;
        vm.Notifications = false;
        vm.SaveCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(1, saves);
        Assert.False(app.Settings.Current.KeepMkv);
        Assert.False(app.Settings.Current.CdLookup);
        Assert.False(app.Settings.Current.Notifications);
    }

    /// <summary>A typed key must look like a key.</summary>
    [Fact]
    public void A_typed_key_must_look_like_a_key()
    {
        var app = new DebugApp();
        var keys = new FakeKeys();
        var vm = app.SettingsScreen(keys);

        vm.TypedKey = "not a key";
        vm.UseTypedKeyCommand.Execute(null);
        Assert.Null(keys.Key);

        vm.TypedKey = "T-" + new string('b', 60);
        vm.UseTypedKeyCommand.Execute(null);
        Assert.Equal("T-" + new string('b', 60), keys.Key);
        Assert.Contains("Key installed", vm.KeyStatus);
    }

    /// <summary>The beta key is fetched and installed.</summary>
    [Fact]
    public async Task The_beta_key_is_fetched_and_installed()
    {
        var app = new DebugApp();
        var keys = new FakeKeys();
        var vm = app.SettingsScreen(keys);
        await vm.GetBetaKeyCommand.ExecuteAsync(null);
        Assert.StartsWith("T-", keys.Key);
        Assert.Contains("end of the month", vm.KeyStatus);
    }

    #endregion
}

public class HistoryViewModelTests
{
    #region Methods

    /// <summary>Clearing the list asks first.</summary>
    [Fact]
    public void Clearing_the_list_asks_first()
    {
        var app = new DebugApp();
        app.History.Videos.Add("DISC", "Movie", Path.GetTempPath(), new[] { "Movie.mkv" }, 0);
        var vm = new HistoryViewModel(app.History.Videos, app.Dialogs, app.Shell);

        app.Dialogs.Answer = false;
        vm.ClearCommand.Execute(null);
        Assert.Single(vm.Entries);

        app.Dialogs.Answer = true;
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Entries);
    }

    /// <summary>Open folder needs a row and an existing folder.</summary>
    [Fact]
    public void Open_folder_needs_a_row_and_an_existing_folder()
    {
        var app = new DebugApp();
        using var dir = new TempDir();
        var entry = app.History.Music.Add("CD", "Artist - Album", dir.Path, new[] { "01 - a.flac" }, 0);
        var vm = new HistoryViewModel(app.History.Music, app.Dialogs, app.Shell);

        vm.OpenFolderCommand.Execute(null);
        Assert.Contains("Click a CD", app.Dialogs.Shown.Single().Message);

        vm.Selected = entry;
        vm.OpenFolderCommand.Execute(null);
        Assert.Equal(dir.Path, app.Shell.Opened.Single());
        Assert.Equal("Album", vm.NameHeader);
    }

    #endregion
}

public class QueueViewModelTests
{
    #region Methods

    /// <summary>The format picker and the header follow the queue.</summary>
    [Fact]
    public void The_format_picker_and_the_header_follow_the_queue()
    {
        var app = new DebugApp();
        app.Queue.SetPaused(true);   //nothing starts compressing during the test
        var vm = new QueueViewModel(app.Queue, app.Tools, app.Settings, app.Presets, app.History, app.Dialogs, app.Notifications, app.Picker);

        Assert.Equal("Compress Queue", vm.Header);
        vm.FormatIndex = 1;
        Assert.True(vm.SaveAsMkv);

        using var dir = new TempDir();
        vm.AddDropped(new[] { dir.File("a.mkv"), dir.File("notes.txt") });
        Assert.Single(app.Queue.Jobs);
        Assert.True(app.Queue.Jobs[0].SaveAsMkv);
        Assert.Equal("Compress Queue (1)", vm.Header);
        Assert.Equal("Resume queue", vm.PauseText);
    }

    #endregion
}

public class RipViewModelTests
{
    #region Methods

    /// <summary>The Keep Ripping! box is saved and follows the settings window.</summary>
    [Fact]
    public void The_do_another_box_is_saved_and_follows_the_settings_window()
    {
        var app = new DebugApp();
        var vm = app.Video();
        app.Settings.Current.AutoNext = true;

        vm.AutoNext = false;
        Assert.False(app.Settings.Current.AutoNext);

        app.Settings.Current.AutoNext = true;
        app.Settings.Save();
        Assert.True(vm.AutoNext);
    }

    /// <summary>Automation locks the Keep Ripping! box.</summary>
    [Fact]
    public async Task Automation_locks_the_do_another_box()
    {
        var app = new DebugApp();
        var vm = app.Video();
        await vm.SetActiveAsync(false);   //don't look at the (pretend) drive: automation would start ripping it
        vm.Automation = true;
        Assert.True(app.Settings.Current.AutomationMode);
        Assert.False(vm.AutoNextEnabled);
        Assert.True(vm.CanChangeAutomation);
        Assert.True(vm.PrimaryEnabled);   //debug mode: the pretend MakeMKV is always there
    }

    /// <summary>The cd side starts idle and ready.</summary>
    [Fact]
    public void The_cd_side_starts_idle_and_ready()
    {
        var app = new DebugApp();
        var vm = app.Cd();
        Assert.Equal(RipState.Idle, vm.State);
        Assert.False(vm.CyanripMissing);
        Assert.True(vm.PrimaryEnabled);
        Assert.Equal("Start", vm.ButtonText);
    }

    #endregion
}

public class MovieFolderTests
{
    #region Methods

    /// <summary>Change opens the Settings page at the output path, and the disc screen uses the new one once it's saved.</summary>
    [Fact]
    public async Task Change_opens_settings_at_the_output_path_and_uses_the_new_one()
    {
        var app = new DebugApp();
        using var dir = new TempDir();
        var disc = new DiscFound(0, "MOVIE", "D:", new[] { new TitleInfo { Id = 0, Seconds = 6000, Duration = "1:40:00", Size = 1 } });
        var review = new DiscReviewViewModel(disc, @"C:\Old", new[] { Presets.Auto }, Presets.Auto, true, _ => null,
                                             changeFolder: async () => await app.Navigation.OpenSettingsAsync(SettingsViewModel.MovieFolderSetting)
                                                 ? app.Settings.Current.MovieFolder : null);
        var change = review.ChangeFolderCommand.ExecuteAsync(null);

        var page = app.Navigation.Settings!;
        Assert.True(app.Navigation.IsSettingsOpen);
        Assert.Equal(SettingsViewModel.MovieFolderSetting, page.Highlight);
        page.MovieFolder = dir.Path;
        page.SaveCommand.Execute(null);
        await change;

        Assert.False(app.Navigation.IsSettingsOpen);
        Assert.Equal(dir.Path, review.Folder);
    }

    /// <summary>Back (cancel) leaves the Settings page without saving.</summary>
    [Fact]
    public async Task Back_leaves_settings_without_saving()
    {
        var app = new DebugApp();
        var closed = app.Navigation.OpenSettingsAsync();
        var page = app.Navigation.Settings!;
        page.AutoNext = !page.AutoNext;
        page.CancelCommand.Execute(null);
        Assert.False(await closed);
        Assert.Null(app.Navigation.Settings);
    }

    /// <summary>The theme picker saves Light, Dark or System.</summary>
    [Fact]
    public void The_theme_is_saved()
    {
        var app = new DebugApp();
        var vm = app.SettingsScreen();
        Assert.Equal(0, vm.ThemeIndex);
        vm.ThemeIndex = 2;
        vm.SaveCommand.Execute(null);
        Assert.Equal("Dark", app.Settings.Current.Theme);
    }

    /// <summary>The movie folder setting is saved.</summary>
    [Fact]
    public void The_movie_folder_setting_is_saved()
    {
        var app = new DebugApp();
        using var dir = new TempDir();
        var vm = app.SettingsScreen();
        vm.MovieFolder = dir.Path;
        vm.SaveCommand.Execute(null);
        Assert.Equal(dir.Path, app.Settings.Current.MovieFolder);
        Assert.Equal(dir.Path, AppFolders.SavedMovieFolder(app.Settings.Current));

        vm = app.SettingsScreen();
        vm.MovieFolder = "not a folder";
        vm.SaveCommand.Execute(null);
        Assert.Equal("Check the output path", app.Dialogs.Shown.Last().Title);
    }

    #endregion
}

public class NotificationTests
{
    #region Methods

    /// <summary>The test button sends one even when notifications are off.</summary>
    [Fact]
    public void The_test_button_sends_one_even_when_notifications_are_off()
    {
        var app = new DebugApp();
        app.Settings.Current.Notifications = false;
        var vm = app.SettingsScreen();
        vm.TestNotificationCommand.Execute(null);
        Assert.Equal("test", app.Notifications.Titles.Single());
        Assert.Empty(app.Dialogs.Shown);
    }

    #endregion
}

public class PresetListTests
{
    #region Methods

    /// <summary>Loading the real presets keeps the chosen one selected.</summary>
    [Fact]
    public async Task Loading_the_real_presets_keeps_the_chosen_one_selected()
    {
        var app = new DebugApp();
        var list = app.Presets.Choices;
        var auto = list[0];
        int removedSelected = 0;
        list.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null && e.OldItems.Contains(auto) && e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Move) removedSelected++;
        };
        await app.Presets.LoadAsync();
        Assert.Equal(Presets.Auto, list[0]);
        Assert.Equal(0, removedSelected);   //"Auto" was never taken out, so a list showing it keeps showing it
        Assert.True(app.Presets.HasChecked);
    }

    /// <summary>Deleting the original is off by default.</summary>
    [Fact]
    public void Deleting_the_original_is_off_by_default()
    {
        Assert.True(new AppSettings().KeepMkv);
        Assert.False(new DebugApp().SettingsScreen().DeleteOriginal);
    }

    #endregion
}

public class CompressedFolderTests
{
    #region Methods

    /// <summary>Without a compressed output path the smaller copy goes next to the rip; with one, into it, keeping the per-disc folder.</summary>
    [Fact]
    public void Compressed_copies_go_to_the_compressed_output_path_with_the_same_disc_folder()
    {
        var settings = new SettingsService(null);
        var folders = new AppFolders(new RunMode(false), settings);
        var rips = Path.Combine(Path.GetTempPath(), "Rips");
        var show = Path.Combine(rips, "The Show");

        Assert.Equal(show, folders.CompressedFolderFor(show, rips));

        var small = Path.Combine(Path.GetTempPath(), "Small");
        settings.Current.CompressedFolder = small;
        Assert.Equal(small, folders.CompressedFolderFor(rips, rips));
        Assert.Equal(Path.Combine(small, "The Show"), folders.CompressedFolderFor(show, rips));

        // debug mode never writes into real folders
        Assert.Equal(show, new AppFolders(new RunMode(true), settings).CompressedFolderFor(show, rips));
    }

    /// <summary>The setting is saved, and a relative path is refused.</summary>
    [Fact]
    public void The_compressed_output_path_is_saved_and_checked()
    {
        var app = new DebugApp();
        using var dir = new TempDir();
        var vm = app.SettingsScreen();
        Assert.Equal("", vm.CompressedFolder);

        vm.CompressedFolder = "not a folder";
        vm.SaveCommand.Execute(null);
        Assert.Equal("Check the compressed output path", app.Dialogs.Shown.Last().Title);

        vm.CompressedFolder = dir.Path;
        vm.SaveCommand.Execute(null);
        Assert.Equal(dir.Path, app.Settings.Current.CompressedFolder);
    }

    #endregion
}
