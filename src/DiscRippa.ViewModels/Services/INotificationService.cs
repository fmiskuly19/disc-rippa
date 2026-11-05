namespace DiscRippa.ViewModels.Services;

/// <summary>Windows notifications and the tray icon's tooltip.</summary>
public interface INotificationService
{
    #region Methods

    /// <summary>A Windows notification (if notifications are switched on in Settings).</summary>
    void Notify(string title, string message);

    /// <summary>Shows a notification even if they're switched off, for the "Send a test notification" button.
    /// Returns a problem description, or null if Windows accepted it.</summary>
    string? Test();

    /// <summary>The tray icon's tooltip, e.g. "DiscRippa — ripping 45%".</summary>
    void SetStatus(string text);

    #endregion
}
