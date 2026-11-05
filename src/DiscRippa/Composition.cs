using DiscRippa.Services;
using DiscRippa.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DiscRippa;

/// <summary>
/// Everything the app is made of, and how the pieces are put together. Debug mode is decided here, once: it swaps the
/// real drives and programs for simulated ones, and keeps the history and queue in memory.
/// </summary>
public static class Composition
{
    #region Methods

    /// <summary>Registers every service, view model and window.</summary>
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        var settings = new SettingsService(AppPaths.SettingsFile);
        var mode = new RunMode(settings.Current.DebugMode);
        mode.ResetDebugData();
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(mode);
        services.AddSingleton<AppFolders>();

        var simulators = mode.IsDebug ? new Simulators(new SimulatedRunner(), new SimulatedCd()) : Simulators.None;
        services.AddSingleton(simulators);
        services.AddSingleton(new ToolRunners(simulators.Video ?? (IProcessRunner)new ProcessRunner(),
                                              simulators.Cd ?? (IProcessRunner)new ProcessRunner()));
        services.AddSingleton<ICdDriveProbe>(simulators.Cd ?? (ICdDriveProbe)new WindowsCdDriveProbe());
        services.AddSingleton<IToolPaths, ToolPaths>();
        services.AddSingleton<IDriveService, DriveService>();
        services.AddSingleton<IMakeMkvKeyService, MakeMkvKeyService>();
        services.AddSingleton(sp => new DiscWatcher(sp.GetRequiredService<ToolRunners>().MakeMkv,
                                                    () => sp.GetRequiredService<IToolPaths>().MakeMkv, mode.VideoPollInterval));
        services.AddSingleton(sp => new CdWatcher(sp.GetRequiredService<ICdDriveProbe>(), mode.CdPollInterval));

        services.AddSingleton(sp =>
        {
            var tools = sp.GetRequiredService<IToolPaths>();
            var queue = new CompressQueue(sp.GetRequiredService<ToolRunners>().HandBrake, mode.QueueFile)
            {
                HandBrakePath = () => tools.HandBrake,
                PreferGraphicsCard = () => settings.Current.UseGraphicsCard,
            };
            queue.Load();
            return queue;
        });
        services.AddSingleton<PresetService>();
        services.AddSingleton(new HistoryStores(new HistoryStore(mode.VideoHistoryFile), new HistoryStore(mode.MusicHistoryFile, music: true)));

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<ThemeService>();

        services.AddSingleton<ToolPicker>();
        services.AddSingleton<VideoRipViewModel>();
        services.AddSingleton<CdRipViewModel>();
        services.AddSingleton<DriveBoxViewModel>();
        services.AddSingleton<QueueViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<Func<SettingsViewModel>>(sp => () => sp.GetRequiredService<SettingsViewModel>());
        services.AddSingleton<Navigation>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    #endregion
}
