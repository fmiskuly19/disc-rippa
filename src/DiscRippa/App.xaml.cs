using System.Windows.Threading;
using DiscRippa.Services;
using DiscRippa.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DiscRippa;

public partial class App : Application
{
    #region Fields

    private Mutex? _singleInstance;
    private ServiceProvider? _services;
    private bool _showingError;
    private string _lastError = "";
    private DateTime _lastErrorShown = DateTime.MinValue;

    #endregion

    #region Methods

    /// <summary>Makes sure only one copy runs, builds the app and shows the main window.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            args.SetObserved();
        };

        if (!TakeSingleInstance(restarted: Array.IndexOf(e.Args, WindowService.RestartArg) >= 0))
        {
            Dialog.Show("The app is already running. Look for it on the taskbar or in the notification area.",
                        "DiscRippa", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _services = Composition.Build();
            _services.GetRequiredService<ThemeService>().Apply();
            var window = _services.GetRequiredService<MainWindow>();
            var tray = _services.GetRequiredService<NotificationService>();
            tray.OpenRequested += window.BringToFront;
            tray.ExitRequested += window.Close;
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            Dialog.Show($"The app couldn't start:\n\n{ex.Message}\n\nDetails were saved to:\n{AppPaths.CrashLog}",
                        "DiscRippa", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Only one copy runs at a time. After a restart the old copy may take a moment to close, so wait for it.</summary>
    private bool TakeSingleInstance(bool restarted)
    {
        _singleInstance = new Mutex(false, @"Local\DiscRippa.SingleInstance");
        try
        {
            return _singleInstance.WaitOne(restarted ? TimeSpan.FromSeconds(20) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true;   //the previous copy closed without letting go; it's ours now
        }
    }

    /// <summary>Removes the tray icon and lets the next copy start.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.Dispose();
        }
        catch
        {
        }   //also removes the tray icon
        try
        {
            _singleInstance?.ReleaseMutex();
        }
        catch
        {
        }
        base.OnExit(e);
    }

    /// <summary>Logs an unexpected error and shows it (the same error at most once a minute).</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception);
        e.Handled = true;

        //one dialog at a time, and the same error at most once a minute: an error that repeats
        //(for example on every redraw) must not bury the screen in pop-ups.
        var message = e.Exception.Message;
        if (_showingError) return;
        if (message == _lastError && DateTime.Now - _lastErrorShown < TimeSpan.FromMinutes(1)) return;

        _showingError = true;
        _lastError = message;
        try
        {
            Dialog.Show($"Something went wrong:\n\n{message}\n\nThe app will keep running. Details were saved to:\n{AppPaths.CrashLog}",
                        "DiscRippa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _lastErrorShown = DateTime.Now;
            _showingError = false;
        }
    }

    #endregion
}
