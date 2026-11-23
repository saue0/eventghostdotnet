using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
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

    private ObservableCollection<MacroGroup> GetGroupSiblings(MacroGroup group) =>
        group.Parent is MacroGroup parent ? parent.Groups : _configurationRoot.Groups;

    private void SynchronizeGroupOrder()
    {
        static IEnumerable<MacroGroup> Walk(IEnumerable<MacroGroup> groups)
        {
            foreach (var group in groups)
            {
                yield return group;
                foreach (var child in Walk(group.Groups))
                    yield return child;
            }
        }

        var ordered = Walk(_configurationRoot.Groups).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var oldIndex = MacroGroups.IndexOf(ordered[index]);
            if (oldIndex < 0)
                MacroGroups.Insert(index, ordered[index]);
            else if (oldIndex != index)
                MacroGroups.Move(oldIndex, index);
        }
    }

    private TreeViewItem? RevealTreeItem(ConfigurationItem target)
    {
        var path = new Stack<ConfigurationItem>();
        for (ConfigurationItem? current = target; current is not null; current = current.Parent)
            path.Push(current);
        ItemsControl parent = ConfigurationTree;
        TreeViewItem? item = null;
        while (path.TryPop(out var part))
        {
            parent.UpdateLayout();
            item = parent.ItemContainerGenerator.ContainerFromItem(part) as TreeViewItem;
            if (item is null)
                return null;
            if (path.Count > 0)
            {
                item.IsExpanded = true;
                item.UpdateLayout();
            }
            parent = item;
        }
        return item;
    }

    private void AddMacroGroup_Click(object sender, RoutedEventArgs e)
    {
        AddMacroGroup("Ajouter un groupe", "Nouveau groupe");
    }

    private void AddMacroGroup(string title, string defaultName)
    {
        var name = PromptForText(title, "Nom du groupe :", defaultName)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (MacroGroups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Un groupe porte déjà ce nom.", "Nom de groupe en double",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var group = new MacroGroup { Name = name };
        var parent = GetSelectedMacroGroup();
        (parent?.Groups ?? _configurationRoot.Groups).Add(group);
        SynchronizeGroupOrder();
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
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
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
        if (group.Macros.Count > 0 || group.Groups.Count > 0)
        {
            MessageBox.Show(this, "Déplacez ou supprimez les macros et sous-groupes avant de supprimer ce groupe.",
                "Groupe non vide", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Supprimer le groupe « {group.Name} » ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        GetGroupSiblings(group).Remove(group);
        MacroGroups.Remove(group);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectMacroGroup(MacroGroups[0]);
        WriteLog($"Groupe supprimé : {group.Name}.");
    }

    private void SelectMacroGroup(MacroGroup group)
    {
        group.IsExpanded = true;
        if (RevealTreeItem(group) is not TreeViewItem item)
            return;
        item.IsExpanded = true;
        item.IsSelected = true;
        item.BringIntoView();
    }
}
