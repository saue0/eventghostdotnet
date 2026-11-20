using System.Windows;

namespace EventGhostDotNet.Items.PluginStartup;

public partial class PluginStartupTemplate : ResourceDictionary
{
    public PluginStartupTemplate() => InitializeComponent();

    private void PluginStartupEnabled_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).PluginStartupEnabled_Click(sender, e);

    private void EditPluginStartupItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).EditPluginStartupItem_Click(sender, e);

    private void RemovePluginStartupItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).RemovePluginStartupItem_Click(sender, e);
}
