using System.Windows.Controls;
using System.Windows.Input;

namespace DiscRippa.Views;

public partial class HistoryView : UserControl
{
    #region Constructors

    /// <summary>Creates a new <see cref="HistoryView"/>.</summary>
    public HistoryView()
    {
        InitializeComponent();

        //grid columns aren't part of the visual tree, so their headings can't be bound; set them here instead.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not HistoryViewModel vm) return;
            NameColumn.Header = vm.NameHeader;
            FilesColumn.Header = vm.FilesHeader;
        };
    }

    #endregion

    #region Methods

    /// <summary>Double-clicking a row opens its folder.</summary>
    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && vm.Selected != null) vm.OpenFolderCommand.Execute(null);
    }

    #endregion
}
