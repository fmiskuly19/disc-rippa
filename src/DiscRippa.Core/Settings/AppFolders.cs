namespace DiscRippa.Core.Settings;

/// <summary>Where rips go to start with: the last folder used, the Videos / Music folders, or the debug test folders.</summary>
public sealed class AppFolders
{
    #region Fields

    private readonly RunMode _mode;
    private readonly ISettingsService _settings;

    #endregion

    #region Properties

    /// <summary>Where ripped movies go (debug mode: the test folder).</summary>
    public string MovieFolder => _mode.IsDebug ? RunMode.VideoTestFolder : SavedMovieFolder(_settings.Current);

    /// <summary>Where Artist\Album folders are made.</summary>
    public string MusicFolder
    {
        get
        {
            if (_mode.IsDebug) return RunMode.MusicTestFolder;
            var saved = _settings.Current.MusicFolder;
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            return music.Length > 0 ? music : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    #endregion

    #region Constructors

    /// <summary>Creates the folder rules for the given mode and settings.</summary>
    public AppFolders(RunMode mode, ISettingsService settings)
    {
        _mode = mode;
        _settings = settings;
    }

    #endregion

    #region Methods

    /// <summary>The Output path setting, or what it stands for when it hasn't been set: the last folder used, else Videos.</summary>
    public static string SavedMovieFolder(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.MovieFolder)) return s.MovieFolder;
        var last = s.LastFolder;
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last)) return last;
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return Directory.Exists(videos) ? videos : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Where the smaller copy of a rip goes: the compressed output path, with the same folder-per-disc as the rip
    /// (several titles get their own folder there too). Next to the rip when no compressed output path is set, and
    /// always in debug mode, so test runs never write into real folders.
    /// </summary>
    public string CompressedFolderFor(string ripFolder, string saveFolder)
    {
        if (_mode.IsDebug) return ripFolder;
        var root = SavedCompressedFolder(_settings.Current);
        if (root == null) return ripFolder;
        try
        {
            var relative = Path.GetRelativePath(saveFolder, ripFolder);
            return relative == "." || relative.StartsWith("..") || Path.IsPathRooted(relative) ? root : Path.Combine(root, relative);
        }
        catch
        {
            return root;
        }
    }

    /// <summary>The compressed output path setting, or null when the smaller copies go next to the originals.</summary>
    public static string? SavedCompressedFolder(AppSettings s)
        => string.IsNullOrWhiteSpace(s.CompressedFolder) ? null : s.CompressedFolder.Trim();

    #endregion
}
