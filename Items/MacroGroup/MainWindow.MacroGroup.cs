using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private MacroGroup? GetSelectedMacroGroup() => ConfigurationTree.SelectedItem switch
    {
        MacroGroup group => group,
        MacroDefinition macro => FindMacroGroup(macro),
        MacroAction action => FindMacroGroup(
            Macros.FirstOrDefault(macro => macro.Actions.Contains(action)) ??
            (StartupActions.Contains(action) ? _startupMacro : null)),
        _ => null
    };

    private MacroGroup? FindMacroGroup(MacroDefinition? macro) =>
        macro is null ? null : MacroGroups.FirstOrDefault(group => group.Id == macro.GroupId);

    private void AddMacroGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForText("Ajouter un groupe", "Nom du groupe :", "Nouveau groupe")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (MacroGroups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Un groupe porte déjà ce nom.", "Nom de groupe en double",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var group = new MacroGroup { Name = name };
        MacroGroups.Add(group);
        SelectMacroGroup(group);
        WriteLog($"Groupe créé : {group.Name}.");
    }

    internal void RenameMacroGroupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MacroGroup group })
            return;

        RenameMacroGroup(group);
    }

    private void RenameMacroGroup(MacroGroup group)
    {
        SelectMacroGroup(group);
        var name = PromptForText("Renommer le groupe", "Nom du groupe :", group.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (MacroGroups.Any(item => item.Id != group.Id &&
                                    string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Un groupe porte déjà ce nom.", "Nom de groupe en double",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        group.Name = name;
        WriteLog($"Groupe renommé : {group.Name}.");
    }

    internal void RemoveMacroGroupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MacroGroup group })
            return;
        SelectMacroGroup(group);
        if (MacroGroups.Count <= 1)
        {
            MessageBox.Show(this, "Il doit rester au moins un groupe de macros.", "Suppression impossible",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (group.Macros.Count > 0)
        {
            MessageBox.Show(this, "Déplacez ou supprimez les macros de ce groupe avant de le supprimer.",
                "Groupe non vide", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Supprimer le groupe « {group.Name} » ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        MacroGroups.Remove(group);
        SelectMacroGroup(MacroGroups[0]);
        WriteLog($"Groupe supprimé : {group.Name}.");
    }

    private void SelectMacroGroup(MacroGroup group)
    {
        group.IsExpanded = true;
        ConfigurationTree.UpdateLayout();
        if (ConfigurationTree.ItemContainerGenerator.ContainerFromItem(group) is not TreeViewItem item)
            return;
        item.IsExpanded = true;
        item.IsSelected = true;
        item.BringIntoView();
    }
}
