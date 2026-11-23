using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EventGhostDotNet;

public partial class NotificationHistoryWindow : Window
{
    private readonly Action _activate;

    public NotificationHistoryWindow(IReadOnlyList<NotificationHistoryEntry> entries, Action activate)
    {
        InitializeComponent();
        _activate = activate;
        HistoryList.ItemsSource = entries;
    }

    private void HistoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(HistoryList, source) is not ListBoxItem)
            return;

        _activate();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
