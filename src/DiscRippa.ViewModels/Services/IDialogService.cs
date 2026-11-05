namespace DiscRippa.ViewModels.Services;

public enum DialogIcon { None, Information, Warning, Error, Question }

/// <summary>Messages, questions and file / folder pickers. The WPF app shows them in its own styled dialog.</summary>
public interface IDialogService
{
    #region Methods

    /// <summary>Shows a message with an OK button.</summary>
    void Show(string message, string title, DialogIcon icon = DialogIcon.Information);

    /// <summary>A Yes / No question. True = Yes. With <paramref name="defaultNo"/>, Enter answers No (for anything risky).</summary>
    bool Confirm(string message, string title, DialogIcon icon = DialogIcon.Question, bool defaultNo = false);

    /// <summary>An OK / Cancel question. True = OK.</summary>
    bool ConfirmOkCancel(string message, string title, DialogIcon icon = DialogIcon.Information);

    /// <summary>The folder chosen, or null if cancelled.</summary>
    string? PickFolder(string title, string? startFolder = null);

    /// <summary>The file(s) chosen, or null if cancelled. <paramref name="filter"/> is "Name|*.ext|All files|*.*".</summary>
    IReadOnlyList<string>? PickFiles(string title, string filter, string? startFolder = null, bool multiple = false);

    #endregion
}
