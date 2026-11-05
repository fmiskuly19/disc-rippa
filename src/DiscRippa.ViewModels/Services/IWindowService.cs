namespace DiscRippa.ViewModels.Services;

/// <summary>The app itself.</summary>
public interface IWindowService
{
    #region Methods

    /// <summary>Closes this copy of the app and starts a fresh one (used to switch debug mode).</summary>
    void Restart();

    #endregion
}
