namespace DiscRippa.ViewModels.Common;

/// <summary>Small text helpers shared by the screens.</summary>
public static class Text
{
    #region Methods

    /// <summary>"1:02:03" or "2:03".</summary>
    public static string Clock(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>The first line of a message (for notifications), or a generic one.</summary>
    public static string FirstLine(string? text)
        => string.IsNullOrEmpty(text) ? "Something went wrong." : text.Split('\n')[0];

    /// <summary>"1 track" or "3 tracks".</summary>
    public static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    #endregion
}
