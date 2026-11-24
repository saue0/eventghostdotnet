using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EventGhostDotNet;

public partial class NotificationHistoryWindow : Window
{
    private readonly Action _activate;
    private readonly Action<NotificationHistoryEntry> _remove;
    private readonly ObservableCollection<NotificationHistoryEntry> _entries;

    public NotificationHistoryWindow(
        IReadOnlyList<NotificationHistoryEntry> entries,
        Action<NotificationHistoryEntry> remove,
        Action activate)
    {
        InitializeComponent();
        _activate = activate;
        _remove = remove;
        _entries = new ObservableCollection<NotificationHistoryEntry>(entries);
        HistoryList.ItemsSource = _entries;
    }

    private void HistoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(HistoryList, source) is not ListBoxItem ||
            IsInsideButton(source))
            return;

        _activate();
        Close();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NotificationHistoryEntry entry)
            return;

        _remove(entry);
        _entries.Remove(entry);
    }

    /// <summary>Un clic sur le bouton de suppression ne doit pas activer EventGhost.</summary>
    private static bool IsInsideButton(DependencyObject source)
    {
        for (DependencyObject? current = source;
             current is not null && current is not ListBoxItem;
             current = current is Visual
                 ? VisualTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
                return true;
        }

        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
