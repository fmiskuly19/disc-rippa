using System.ComponentModel;
using System.Windows.Controls;

namespace DiscRippa.Views;

/// <summary>The main window. Everything it shows comes from <see cref="MainViewModel"/>; this is only the window's life cycle.</summary>
public partial class MainWindow : Window
{
    #region Fields

    private readonly MainViewModel _vm;

    #endregion

    #region Constructors

    /// <summary>Creates a new <see cref="MainWindow"/>.</summary>
    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.StartAsync();
    }

    #endregion

    #region Methods

    /// <summary>From the tray icon: show the window and put it in front.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Files may have been moved or deleted since the list was last shown.</summary>
    private void VideoTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, VideoTabs) && VideoTabs.SelectedItem == VideoHistoryTab) _vm.VideoHistory.Refresh();
    }

    /// <summary>Files may have been moved or deleted since the list was last shown.</summary>
    private void CdTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, CdTabs) && CdTabs.SelectedItem == MusicHistoryTab) _vm.MusicHistory.Refresh();
    }

    /// <summary>Asks before closing during a rip or compression, then stops everything.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_vm.CanClose())
        {
            e.Cancel = true;
            return;
        }
        _vm.Shutdown();
        base.OnClosing(e);
    }

    #endregion
}
