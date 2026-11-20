using System.Windows;

namespace EventGhostDotNet.Items.MacroEvent;

public partial class MacroEventTemplate : ResourceDictionary
{
    public MacroEventTemplate() => InitializeComponent();

    private void EditEventItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).EditEventItem_Click(sender, e);

    private void RemoveEventItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).RemoveEventItem_Click(sender, e);
}
