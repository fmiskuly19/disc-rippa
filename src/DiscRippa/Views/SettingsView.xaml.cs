using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DiscRippa.Views;

public partial class SettingsView : UserControl
{
    #region Constructors

    /// <summary>Creates the page; it scrolls to and flashes the setting its view model asks for each time it's shown.</summary>
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not SettingsViewModel vm) return;
            if (vm.Highlight == SettingsViewModel.MovieFolderSetting)
            {
                SystemTab.IsSelected = true;
                Dispatcher.BeginInvoke(new Action(() => Flash(MovieFolderSetting, MovieFolderBox)),
                                       System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                Tabs.SelectedIndex = 0;
            }
        };
    }

    #endregion

    #region Methods

    /// <summary>Scrolls a setting into view and pulses it in the accent colour, so it's obvious what to change.</summary>
    private void Flash(Border setting, Control focus)
    {
        setting.BringIntoView();
        focus.Focus();
        if (focus is TextBox box) box.SelectAll();

        var accent = ((SolidColorBrush)FindResource("AccentTextBrush")).Color;
        var soft = ((SolidColorBrush)FindResource("AccentSoftBrush")).Color;
        var border = new SolidColorBrush(accent);
        var fill = new SolidColorBrush(soft);
        setting.BorderBrush = border;
        setting.Background = fill;

        var fade = new ColorAnimation { To = Colors.Transparent, BeginTime = TimeSpan.FromSeconds(2.5), Duration = TimeSpan.FromSeconds(1.2) };
        border.BeginAnimation(SolidColorBrush.ColorProperty, fade);
        fill.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }

    #endregion
}
