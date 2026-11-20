using System.Windows;

namespace EventGhostDotNet.Items.MacroGroup;

public partial class MacroGroupTemplate : ResourceDictionary
{
    public MacroGroupTemplate() => InitializeComponent();

    private void RenameMacroGroupItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).RenameMacroGroupItem_Click(sender, e);

    private void RemoveMacroGroupItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).RemoveMacroGroupItem_Click(sender, e);
}
