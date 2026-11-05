using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;

namespace DiscRippa.Converters;

/// <summary>The view model's <see cref="TaskbarProgress"/> as WPF's taskbar progress state.</summary>
public sealed class TaskbarStateConverter : IValueConverter
{
    #region Methods

    /// <summary>Converts the value for the screen.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        TaskbarProgress.Normal => TaskbarItemProgressState.Normal,
        TaskbarProgress.Indeterminate => TaskbarItemProgressState.Indeterminate,
        _ => TaskbarItemProgressState.None,
    };

    /// <summary>Not used: these bindings only go one way.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    #endregion
}

/// <summary>Visible when false, collapsed when true (the opposite of BooleanToVisibilityConverter).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    #region Methods

    /// <summary>Converts the value for the screen.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Not used: these bindings only go one way.</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    #endregion
}
