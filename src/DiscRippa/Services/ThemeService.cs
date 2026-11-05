using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DiscRippa.Services;

/// <summary>
/// Light or dark: swaps the colour dictionary (Themes/Light.xaml or Themes/Dark.xaml) live, and gives every window a
/// title bar to match. Follows the Theme setting, which can also be "match Windows".
/// </summary>
public sealed class ThemeService
{
    #region Fields

    private const int DwmUseImmersiveDarkMode = 20;
    private readonly ISettingsService _settings;
    private bool _dark;
    private bool? _applied;   //the theme now in use; null until the first Apply

    #endregion

    #region Constructors

    /// <summary>Applies the theme now, and again whenever the settings are saved.</summary>
    public ThemeService(ISettingsService settings)
    {
        _settings = settings;
        settings.Saved += Apply;
        // every window (main, dialogs) gets the matching title bar as it opens
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                                          new RoutedEventHandler((sender, _) => SetTitleBar((Window)sender)));
    }

    #endregion

    #region Methods

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Switches to the theme the settings ask for.</summary>
    public void Apply()
    {
        _dark = _settings.Current.Theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => WindowsUsesDarkTheme(),
        };

        var app = Application.Current;
        if (app == null) return;

        //swapping the colours makes every element in the window look them up again, which takes a moment, so it's only
        //done when the theme really changes (settings are saved for all sorts of reasons, like switching modes)
        if (_applied == _dark) return;
        _applied = _dark;

        var dictionaries = app.Resources.MergedDictionaries;
        var source = new Uri($"pack://application:,,,/Themes/{(_dark ? "Dark" : "Light")}.xaml");
        int at = -1;
        for (int i = 0; i < dictionaries.Count; i++)
            if (dictionaries[i].Source?.OriginalString.Contains("Themes/") == true) at = i;
        if (!_dark && at >= 0 && dictionaries[at].Source?.OriginalString.EndsWith("Light.xaml") == true) return;   //already light
        var theme = new ResourceDictionary { Source = source };
        if (at >= 0) dictionaries[at] = theme;
        else dictionaries.Insert(0, theme);

        foreach (Window window in app.Windows) SetTitleBar(window);
    }

    /// <summary>Windows' own "app mode" setting (Settings › Personalisation › Colours).</summary>
    private static bool WindowsUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Gives the window a dark or light title bar to match the theme.</summary>
    private void SetTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int value = _dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
            //an older Windows without dark title bars: the window just keeps the standard one
        }
    }

    #endregion
}
