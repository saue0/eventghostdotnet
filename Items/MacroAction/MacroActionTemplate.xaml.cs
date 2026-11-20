using System.Windows;

namespace EventGhostDotNet.Items.MacroAction;

public partial class MacroActionTemplate : ResourceDictionary
{
    public MacroActionTemplate() => InitializeComponent();

    private void EditActionItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).EditActionItem_Click(sender, e);

    private void RemoveActionItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).RemoveActionItem_Click(sender, e);
}
