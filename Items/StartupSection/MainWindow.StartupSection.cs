using System.Windows;
using System.Windows.Controls;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private Task ExecuteStartupActionsAsync() =>
        StartupActions.Count == 0
            ? Task.CompletedTask
            : ExecuteMacroInSessionAsync(_startupMacro, _sessionCancellation.Token);

    internal async void AddStartupAction_Click(object sender, RoutedEventArgs e)
    {
        var choice = PromptForActionKind();
        if (choice is null)
            return;

        var action = choice.Kind == MacroActionKind.Plugin
            ? ConfigurePluginAction(choice.PluginId!, choice.ActionId!, null, choice.InstanceId)
            : PromptForAction(choice.Kind, sourceMacro: _startupMacro);
        if (action is null)
            return;

        StartupActions.Add(action);
        var startupCount = PluginStartups.Count;
        if (action.Kind == MacroActionKind.Plugin)
            EnsurePluginStartupConfiguration(action.PluginId);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectStartupActionItem(action);
        WriteLog($"Action ajoutée au démarrage : {action.DisplayName}.");
        if (PluginStartups.Count != startupCount && IsLoaded)
            await RestartConfiguredPluginsAsync();
    }

    private void SelectStartupActionItem(MacroAction action)
    {
        if (RevealTreeItem(_startupSection) is not TreeViewItem parent)
            return;
        parent.IsExpanded = true;
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(action) is TreeViewItem child)
        {
            child.IsSelected = true;
            child.BringIntoView();
        }
    }

    private void SelectStartupSection()
    {
        if (RevealTreeItem(_startupSection) is TreeViewItem item)
            item.IsSelected = true;
    }

    private void SetStartupSectionExpanded(bool isExpanded)
    {
        _startupSection.IsExpanded = isExpanded;
        if (RevealTreeItem(_startupSection) is TreeViewItem item)
            item.IsExpanded = isExpanded;
    }
}
