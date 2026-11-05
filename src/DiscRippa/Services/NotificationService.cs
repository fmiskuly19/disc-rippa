using Microsoft.Toolkit.Uwp.Notifications;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DiscRippa.Services;

/// <summary>
/// Windows notifications (proper toasts, kept in the notification centre, with the app's name and icon) and the
/// notification-area icon. The old tray "balloon" is only a fallback: Windows 10/11 often swallows those without a
/// trace. Notifications follow the setting in Settings; the tray tooltip is always kept up.
/// </summary>
public sealed class NotificationService : INotificationService, IDisposable
{
    #region Fields

    private readonly ISettingsService _settings;
    private readonly Forms.NotifyIcon _icon;
    private bool _toastsWork = true;

    #endregion

    #region Constructors

    /// <summary>Creates the tray icon and gets notifications ready.</summary>
    public NotificationService(ISettingsService settings)
    {
        _settings = settings;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DiscRippa",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
        };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();

        //clicking a notification brings the window back. (Windows calls this on a background thread.)
        try
        {
            ToastNotificationManagerCompat.OnActivated += _ =>
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => OpenRequested?.Invoke()));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            _toastsWork = false;
        }
    }

    #endregion

    #region Events

    /// <summary>The tray icon (or a notification) was clicked, or "Open DiscRippa" chosen.</summary>
    public event Action? OpenRequested;

    /// <summary>"Exit" was chosen on the tray icon's menu.</summary>
    public event Action? ExitRequested;

    #endregion

    #region Methods

    /// <summary>Shows a notification if they are switched on in Settings.</summary>
    public void Notify(string title, string message)
    {
        if (!_settings.Current.Notifications) return;
        Show(title, message);
    }

    /// <summary>Shows a notification even if they are off. Returns what went wrong, or null.</summary>
    public string? Test()
    {
        var problem = Show("Notifications work", "This is what you'll see when a disc or a compression finishes.");
        return problem;
    }

    /// <summary>A toast, or a tray balloon if toasts can't be shown. Returns what went wrong, or null.</summary>
    private string? Show(string title, string message)
    {
        if (_toastsWork)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(Trim(title, 120))
                    .AddText(Trim(string.IsNullOrWhiteSpace(message) ? " " : message, 400))
                    .Show();
                return null;
            }
            catch (Exception ex)
            {
                _toastsWork = false;   //don't keep trying; the balloon below will do
                CrashLog.Write(ex);
            }
        }

        try
        {
            _icon.BalloonTipTitle = Trim(title, 63);
            _icon.BalloonTipText = Trim(string.IsNullOrWhiteSpace(message) ? " " : message, 255);
            _icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            _icon.ShowBalloonTip(10000);
            return "Windows wouldn't take a proper notification, so an old-style tray message was used instead.";
        }
        catch (Exception ex)
        {
            return ex.Message;   //a notification that can't be shown must never break a rip
        }
    }

    /// <summary>Sets the tray icon's tooltip.</summary>
    public void SetStatus(string text)
    {
        try
        {
            _icon.Text = Trim(text, 63);
        }
        catch
        {
        }
    }

    /// <summary>The text, cut to <paramref name="max"/> characters with an ellipsis.</summary>
    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>The app icon for the tray.</summary>
    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info != null)
            {
                using var stream = info.Stream;
                return new Drawing.Icon(stream);
            }
        }
        catch
        {
        }
        return Drawing.SystemIcons.Application;
    }

    /// <summary>Removes the tray icon.</summary>
    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch
        {
        }
    }

    #endregion
}
