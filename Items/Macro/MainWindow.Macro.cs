using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using EventGhostDotNet.Core.Macros;
using EventGhostDotNet.PluginContracts;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private void AddMacro_Click(object sender, RoutedEventArgs e)
    {
        var details = PromptForMacroDetails(
            new MacroDefinition { Name = "Nouvelle macro" }, isCreating: true);
        if (details is null)
            return;

        var group = GetSelectedMacroGroup() ?? MacroGroups.FirstOrDefault();
        if (group is null)
            throw new InvalidOperationException("Aucun groupe de macros n'est disponible.");

        var macro = new MacroDefinition { Name = details.Value.Name };
        macro.SetEventNames(details.Value.EventNames);
        AddMacroToGroup(macro, group);
        SelectTreeItem(macro);
        WriteLog($"Macro créée : {macro.Name} (événements : {DescribeMacroEvents(macro)}).");
    }

    private void AddMacroToGroup(MacroDefinition macro, MacroGroup group)
    {
        macro.GroupId = group.Id;
        group.Macros.Add(macro);
        SynchronizeMacroOrder();
    }

    private void SynchronizeMacroOrder()
    {
        var orderedMacros = MacroGroups.SelectMany(group => group.Macros).ToArray();
        for (var index = 0; index < orderedMacros.Length; index++)
        {
            var currentIndex = Macros.IndexOf(orderedMacros[index]);
            if (currentIndex < 0)
                Macros.Insert(index, orderedMacros[index]);
            else if (currentIndex != index)
                Macros.Move(currentIndex, index);
        }
        while (Macros.Count > orderedMacros.Length)
            Macros.RemoveAt(Macros.Count - 1);
    }

    private void MoveMacroToGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigurationTree.SelectedItem is not MacroDefinition macro ||
            FindMacroGroup(macro) is not MacroGroup currentGroup)
            return;

        var destinationGroups = MacroGroups.Where(group => group.Id != currentGroup.Id).ToArray();
        if (destinationGroups.Length == 0)
            return;

        var dialog = new Window
        {
            Title = "Déplacer la macro",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(18)
        };
        var panel = new StackPanel { Width = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Choisissez le groupe de destination pour « {macro.Name} » :",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var groupPicker = new System.Windows.Controls.ComboBox
        {
            ItemsSource = destinationGroups,
            DisplayMemberPath = nameof(MacroGroup.Name),
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(groupPicker);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var move = new Button { Content = "Déplacer", IsDefault = true };
        move.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(move);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        if (dialog.ShowDialog() != true || groupPicker.SelectedItem is not MacroGroup destinationGroup)
            return;

        currentGroup.Macros.Remove(macro);
        macro.GroupId = destinationGroup.Id;
        destinationGroup.Macros.Add(macro);
        SynchronizeMacroOrder();
        SelectTreeItem(macro);
        WriteLog($"Macro « {macro.Name} » déplacée dans « {destinationGroup.Name} ».");
    }

    private static string DescribeMacroEvents(MacroDefinition macro) =>
        macro.EventNames.Count == 0 ? "aucun — exécution manuelle" : macro.EventNamesDisplay;

    private void RemoveMacro_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMacro is not MacroDefinition macro)
            return;

        if (MessageBox.Show(this, $"Supprimer la macro « {macro.Name} » ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        FindMacroGroup(macro)?.Macros.Remove(macro);
        Macros.Remove(macro);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        if (Macros.Count > 0)
            SelectTreeItem(Macros[0]);
        WriteLog($"Macro supprimée : {macro.Name}.");
    }

    private void EditMacro_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMacro is not MacroDefinition macro)
            return;

        var updatedMacro = PromptForMacroDetails(macro);
        if (updatedMacro is null)
            return;

        macro.Name = updatedMacro.Value.Name;
        macro.SetEventNames(updatedMacro.Value.EventNames);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectTreeItem(macro);
        WriteLog($"Macro modifiée : {macro.Name} (événements : {DescribeMacroEvents(macro)}).");
    }

    private async void RunMacro_Click(object sender, RoutedEventArgs e)
    {
        // Une exécution demandée depuis l'arborescence est une racine du journal : sa ligne part
        // du niveau zéro et la macro exécutée s'indente d'un cran, ses actions d'un second.
        const int manualExecutionIndentLevel = 1;
        switch (ConfigurationTree.SelectedItem)
        {
            case MacroDefinition macro:
                PostLog($"Exécution manuelle de la macro « {macro.Name} ».");
                await ExecuteMacroInSessionAsync(macro, _sessionCancellation.Token,
                    logIndentLevel: manualExecutionIndentLevel);
                break;
            case MacroEvent macroEvent:
                PostLog($"Exécution manuelle de la macro « {macroEvent.Macro.Name} » " +
                         $"(événement « {macroEvent.Name} »).");
                await ExecuteMacroInSessionAsync(macroEvent.Macro, _sessionCancellation.Token,
                    logIndentLevel: manualExecutionIndentLevel);
                break;
            case MacroAction action:
                var ownerMacro = Macros.FirstOrDefault(macro => macro.Actions.Contains(action)) ??
                                 (StartupActions.Contains(action) ? _startupMacro : null);
                if (ownerMacro is not null)
                {
                    PostLog($"Exécution manuelle de l'action « {action.DisplayName} » " +
                             $"de la macro « {ownerMacro.Name} ».");
                    // Le moteur place déjà l'entrée et les messages d'une action seule au premier
                    // niveau : ils restent ainsi directement sous la ligne d'exécution manuelle.
                    await ExecuteMacroInSessionAsync(ownerMacro, _sessionCancellation.Token, action.Id);
                }
                break;
        }
    }

    private void SelectTreeItem(MacroDefinition macro, object? item = null)
    {
        var group = FindMacroGroup(macro);
        if (group is null)
            return;
        group.IsExpanded = true;
        if (RevealTreeItem(group) is not TreeViewItem groupItem)
            return;

        groupItem.IsExpanded = true;
        groupItem.UpdateLayout();
        if (groupItem.ItemContainerGenerator.ContainerFromItem(macro) is not TreeViewItem parent)
            return;
        if (item is null)
        {
            parent.IsSelected = true;
            parent.BringIntoView();
            return;
        }

        parent.IsExpanded = true;
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem child)
        {
            child.IsSelected = true;
            child.BringIntoView();
        }
    }

    internal void EditMacroItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroDefinition macro })
        {
            SelectTreeItem(macro);
            EditMacro_Click(sender, e);
        }
    }

    internal void RemoveMacroItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroDefinition macro })
        {
            SelectTreeItem(macro);
            RemoveMacro_Click(sender, e);
        }
    }

    private async Task ExecuteMacroInSessionAsync(
        MacroDefinition macro, CancellationToken cancellationToken, Guid? onlyActionId = null,
        TriggerEvent? triggerEvent = null, int logIndentLevel = 0, CancellationToken? actionContextToken = null)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var execution = _macroEngine.ExecuteAsync(macro, cancellationToken, onlyActionId: onlyActionId,
            triggerEvent: triggerEvent, logIndentLevel: logIndentLevel, actionContextToken: actionContextToken);
        _activeMacroExecutions.Add(execution);
        try
        {
            await execution;
        }
        finally
        {
            _activeMacroExecutions.Remove(execution);
        }
    }

    private (string Name, IReadOnlyList<string> EventNames)? PromptForMacroDetails(
        MacroDefinition macro, bool isCreating = false)
    {
        var nameBox = new TextBox { Text = macro.Name, Padding = new Thickness(6) };
        var eventNames = new ObservableCollection<string>(macro.EventNames);
        var eventNameBox = new TextBox { Padding = new Thickness(6), MinWidth = 250 };
        var dialog = new Window
        {
            Title = isCreating ? "Nouvelle macro" : "Modifier la macro",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(16)
        };
        var panel = new StackPanel { Width = 420 };
        panel.Children.Add(new TextBlock { Text = "Nom de la macro :", Margin = new Thickness(0, 0, 0, 5) });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Événements déclencheurs (facultatifs) :", Margin = new Thickness(0, 12, 0, 5) });
        panel.Children.Add(new TextBlock
        {
            Text = "Sans événement, la macro peut être exécutée manuellement.",
            Foreground = EventGhostDotNet.Theme.AppTheme.GetBrush("MutedTextBrush", Brushes.Gray),
            Margin = new Thickness(0, 0, 0, 5)
        });
        var eventList = new ListBox { ItemsSource = eventNames, Height = 100 };
        panel.Children.Add(eventList);
        var eventControls = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var addEvent = new Button { Content = "Ajouter" };
        var removeEvent = new Button { Content = "Supprimer" };
        DockPanel.SetDock(addEvent, Dock.Right);
        DockPanel.SetDock(removeEvent, Dock.Right);
        eventControls.Children.Add(removeEvent);
        eventControls.Children.Add(addEvent);
        eventControls.Children.Add(eventNameBox);
        panel.Children.Add(eventControls);
        addEvent.Click += (_, _) =>
        {
            var eventName = eventNameBox.Text.Trim();
            if (eventName.Length == 0)
            {
                MessageBox.Show(dialog, "Saisissez un nom d'événement.", "Événement requis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (eventNames.Contains(eventName, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show(dialog, "Cet événement est déjà présent.", "Événement déjà présent",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            eventNames.Add(eventName);
            eventNameBox.Clear();
        };
        removeEvent.Click += (_, _) =>
        {
            if (eventList.SelectedItem is string eventName)
                eventNames.Remove(eventName);
            else
                MessageBox.Show(dialog, "Sélectionnez un événement à supprimer.", "Aucun événement sélectionné",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button
        {
            Content = "Enregistrer",
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text) || eventNames.Any(string.IsNullOrWhiteSpace))
            {
                MessageBox.Show(dialog, "Le nom de la macro est obligatoire et chaque événement doit être non vide.",
                    "Informations manquantes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(eventNameBox.Text))
            {
                MessageBox.Show(dialog, "Ajoutez l'événement saisi à la liste ou effacez-le avant d'enregistrer.",
                    "Événement non ajouté", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        return dialog.ShowDialog() == true
            ? (nameBox.Text.Trim(), eventNames.ToArray())
            : null;
    }
}
