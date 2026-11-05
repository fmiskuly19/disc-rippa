using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DiscRippa.Views;

/// <summary>
/// <c>views:AutoScroll.ToEnd="True"</c> on a ListBox keeps its newest line in view (the rip message logs). The scroll waits
/// until the list has taken in the new line: touching the list from inside the change notification throws
/// "ItemsControl is inconsistent".
/// </summary>
public static class AutoScroll
{
    #region Fields

    public static readonly DependencyProperty ToEndProperty = DependencyProperty.RegisterAttached(
        "ToEnd", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false, OnToEndChanged));

    #endregion

    #region Methods

    /// <summary>Whether the list keeps its newest line in view.</summary>
    public static bool GetToEnd(DependencyObject d) => (bool)d.GetValue(ToEndProperty);

    /// <summary>Makes the list keep its newest line in view.</summary>
    public static void SetToEnd(DependencyObject d, bool value) => d.SetValue(ToEndProperty, value);

    /// <summary>Starts following the list's new lines.</summary>
    private static void OnToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list || e.NewValue is not true) return;
        bool pending = false;
        ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add || pending) return;
            pending = true;
            list.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                pending = false;
                if (list.Items.Count > 0) list.ScrollIntoView(list.Items[list.Items.Count - 1]);
            }));
        };
    }

    #endregion
}
