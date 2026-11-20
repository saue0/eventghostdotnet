using System.Windows;

namespace EventGhostDotNet.Items.StartupSection;

public partial class StartupSectionTemplate : ResourceDictionary
{
    public StartupSectionTemplate() => InitializeComponent();

    private void AddPluginStartup_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).AddPluginStartup_Click(sender, e);

    private void AddStartupAction_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).AddStartupAction_Click(sender, e);
}
