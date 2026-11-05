using DiscRippa.Views;
using Microsoft.Win32;

namespace DiscRippa.Services;

/// <summary>Messages and questions in the app's own styled dialog; files and folders with the Windows pickers.</summary>
public sealed class DialogService : IDialogService
{
    #region Methods

    /// <summary>Shows a message in the app's own dialog.</summary>
    public void Show(string message, string title, DialogIcon icon = DialogIcon.Information)
        => Dialog.Show(Owner(), message, title, MessageBoxButton.OK, Image(icon));

    /// <summary>Asks a Yes / No question. True = Yes.</summary>
    public bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question, bool defaultNo = false)
        => Dialog.Show(Owner(), message, title, MessageBoxButton.YesNo, Image(icon),
                       defaultNo ? MessageBoxResult.No : MessageBoxResult.Yes) == MessageBoxResult.Yes;

    /// <summary>Asks an OK / Cancel question. True = OK.</summary>
    public bool ConfirmOkCancel(string message, string title, DialogIcon icon = DialogIcon.Information)
        => Dialog.Show(Owner(), message, title, MessageBoxButton.OKCancel, Image(icon)) == MessageBoxResult.OK;

    /// <summary>The folder picked in Windows' folder picker, or null.</summary>
    public string? PickFolder(string title, string? startFolder = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (startFolder != null && Directory.Exists(startFolder)) dialog.InitialDirectory = startFolder;
        return dialog.ShowDialog(Owner()) == true ? dialog.FolderName : null;
    }

    /// <summary>The files picked in Windows' file picker, or null.</summary>
    public IReadOnlyList<string>? PickFiles(string title, string filter, string? startFolder = null, bool multiple = false)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = multiple };
        if (startFolder != null && Directory.Exists(startFolder)) dialog.InitialDirectory = startFolder;
        return dialog.ShowDialog(Owner()) == true ? dialog.FileNames : null;
    }

    /// <summary>The window in front (Settings, when it's open), so dialogs sit on top of the right one.</summary>
    private static Window? Owner()
    {
        var app = Application.Current;
        if (app == null) return null;
        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible) ?? app.MainWindow;
    }

    /// <summary>The message box image for a dialog icon.</summary>
    private static MessageBoxImage Image(DialogIcon icon) => icon switch
    {
        DialogIcon.Information => MessageBoxImage.Information,
        DialogIcon.Warning => MessageBoxImage.Warning,
        DialogIcon.Error => MessageBoxImage.Error,
        DialogIcon.Question => MessageBoxImage.Question,
        _ => MessageBoxImage.None,
    };

    #endregion
}
