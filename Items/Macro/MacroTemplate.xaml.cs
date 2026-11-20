using System.Windows;

namespace EventGhostDotNet.Items.Macro;

public partial class MacroTemplate : ResourceDictionary
{
    public MacroTemplate() => InitializeComponent();

    private void EditMacroItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).EditMacroItem_Click(sender, e);

    private void RemoveMacroItem_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow((DependencyObject)sender)).RemoveMacroItem_Click(sender, e);
}
