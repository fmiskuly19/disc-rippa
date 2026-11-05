namespace DiscRippa.ViewModels.Services;

/// <summary>Explorer and the browser.</summary>
public interface IShellService
{
    #region Methods

    /// <summary>Opens <paramref name="folder"/> in Explorer, with <paramref name="selectFile"/> highlighted if it's there.</summary>
    void OpenFolder(string folder, string? selectFile = null);

    /// <summary>Opens a web page in the default browser.</summary>
    void OpenUrl(string url);

    #endregion
}
