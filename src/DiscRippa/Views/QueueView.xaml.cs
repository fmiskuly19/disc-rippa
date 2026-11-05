using System.Windows.Controls;

namespace DiscRippa.Views;

/// <summary>Only the parts WPF can't bind: the grid's multi-selection, and dropped files.</summary>
public partial class QueueView : UserControl
{
    #region Properties

    private QueueViewModel? Vm => DataContext as QueueViewModel;

    #endregion

    #region Constructors

    /// <summary>Creates a new <see cref="QueueView"/>.</summary>
    public QueueView() => InitializeComponent();

    #endregion

    #region Methods

    /// <summary>Passes the selected rows to the view model.</summary>
    private void JobsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm != null) Vm.SelectedJobs = JobsGrid.SelectedItems.OfType<CompressJob>().ToList();
    }

    /// <summary>Accepts dropped files while HandBrakeCLI is there.</summary>
    private void Area_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && Vm?.HandBrakeFound == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Adds dropped files to the queue.</summary>
    private void Area_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Vm?.AddDropped(files);
    }

    #endregion
}
