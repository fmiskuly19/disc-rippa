namespace DiscRippa.ViewModels.Common;

/// <summary>The live message log under a rip (MakeMKV's or cyanrip's), newest last, timestamped, capped in length.</summary>
public sealed class LogViewModel
{
    #region Fields

    private const int MaxLines = 1000;

    #endregion

    #region Properties

    public ObservableCollection<string> Lines { get; } = new();

    #endregion

    #region Methods

    /// <summary>Adds a timestamped line, dropping the oldest past the limit.</summary>
    public void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Lines.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (Lines.Count > MaxLines) Lines.RemoveAt(0);
    }

    /// <summary>Empties the log.</summary>
    public void Clear() => Lines.Clear();

    #endregion
}
