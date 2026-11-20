using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
        switch (ConfigurationTree.SelectedItem)
        {
            case MacroDefinition macro:
                await ExecuteMacroInSessionAsync(macro, _sessionCancellation.Token);
                break;
            case MacroAction action:
                var ownerMacro = Macros.FirstOrDefault(macro => macro.Actions.Contains(action)) ??
                                 (StartupActions.Contains(action) ? _startupMacro : null);
                if (ownerMacro is not null)
                    await ExecuteMacroInSessionAsync(ownerMacro, _sessionCancellation.Token, action.Id);
                break;
        }
    }

    private void SelectTreeItem(MacroDefinition macro, object? item = null)
    {
        var group = FindMacroGroup(macro);
        if (group is null)
            return;
        group.IsExpanded = true;
        ConfigurationTree.UpdateLayout();
        if (ConfigurationTree.ItemContainerGenerator.ContainerFromItem(group) is not TreeViewItem groupItem)
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
        TriggerEvent? triggerEvent = null)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var execution = ExecuteMacroAsync(macro, cancellationToken, onlyActionId: onlyActionId, triggerEvent: triggerEvent);
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

    private async Task ExecuteMacroAsync(
        MacroDefinition macro, CancellationToken cancellationToken, int callDepth = 0, Guid? onlyActionId = null,
        TriggerEvent? triggerEvent = null)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
        if (callDepth >= MaximumMacroCallDepth)
            throw new InvalidOperationException($"La profondeur maximale d'appel de macro ({MaximumMacroCallDepth}) est dépassée.");

        var selectedAction = onlyActionId is Guid selectedActionId
            ? macro.Actions.FirstOrDefault(action => action.Id == selectedActionId)
            : null;
        if (onlyActionId is not null && selectedAction is null)
            throw new InvalidOperationException("L'action sélectionnée n'appartient plus à la macro.");

        WriteLog(selectedAction is null
            ? $"Exécution de la macro « {macro.Name} »."
            : $"Exécution de l'action « {selectedAction.DisplayName} ».");
        var actionIds = selectedAction is null
            ? macro.Actions.Select(action => action.Id).ToArray()
            : new[] { selectedAction.Id };
        var actionIndex = 0;
        var actionExecutions = 0;
        object? previousActionResult = null;
        while (actionIndex < actionIds.Length)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            if (++actionExecutions > MaximumActionExecutionsPerMacro)
            {
                WriteLog($"Limite de {MaximumActionExecutionsPerMacro} exécutions d'actions atteinte dans « {macro.Name} ».",
                    LogLevel.Warning);
                break;
            }

            var actionId = actionIds[actionIndex];
            var action = macro.Actions.FirstOrDefault(item => item.Id == actionId);
            if (action is null || !action.IsEnabled)
            {
                previousActionResult = null;
                actionIndex++;
                continue;
            }

            var nextActionIndex = actionIndex + 1;
            var stopAfterJump = false;
            var actionContext = new ActionContext(
                this,
                cancellationToken,
                action.Kind == MacroActionKind.If
                    ? previousActionResult
                    : action.Value.Length == 0 ? null : action.Value,
                triggerEvent);
            try
            {
                switch (action.Kind)
                {
                    case MacroActionKind.Log:
                        actionContext.Log(action.Value, action.LogLevel ?? LogLevel.Information);
                        break;
                    case MacroActionKind.Delay:
                        if (!int.TryParse(action.Value, out var milliseconds) || milliseconds is < 0 or > 600000)
                            throw new InvalidOperationException($"Durée d'attente invalide : {action.Value}");
                        await Task.Delay(milliseconds, cancellationToken);
                        break;
                    case MacroActionKind.Beep:
                        System.Media.SystemSounds.Beep.Play();
                        break;
                    case MacroActionKind.ShowMessage:
                        MessageBox.Show(this, action.Value, macro.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                    case MacroActionKind.OpenUrl:
                        if (!Uri.TryCreate(action.Value, UriKind.Absolute, out var uri) ||
                            uri.Scheme is not ("http" or "https"))
                            throw new InvalidOperationException($"URL non autorisée : {action.Value}");
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                        break;
                    case MacroActionKind.PowerShell:
                        await RunPowerShellAsync(action.Value, action.UnlimitedExecution, action.UseWindowsPowerShell,
                            actionContext, cancellationToken);
                        break;
                    case MacroActionKind.RaiseEvent:
                        if (string.IsNullOrWhiteSpace(action.Target))
                            throw new InvalidOperationException("Le nom de l'événement est vide.");
                        actionContext.RaiseEvent(action.Target,
                            string.IsNullOrEmpty(action.Value) ? null : action.Value);
                        break;
                    case MacroActionKind.Plugin:
                        var plugin = _pluginLoader.Find(action.PluginId)
                            ?? throw new InvalidOperationException($"Plugin introuvable : {action.PluginId}");
                        EnsurePluginActionEnabled(plugin, action);
                        await plugin.ExecuteActionAsync(ResolvePluginAction(plugin, action),
                            new PluginHost(this, actionContext), cancellationToken);
                        break;
                    case MacroActionKind.EnableMacro:
                    case MacroActionKind.DisableMacro:
                    case MacroActionKind.EnableAction:
                    case MacroActionKind.DisableAction:
                        var result = ExecuteSystemAction(Macros, action);
                        _cleanConfiguration = AcceptRuntimeStateChange(_cleanConfiguration, result);
                        actionContext.Log(result.Message);
                        break;
                    case MacroActionKind.Jump:
                        switch (action.JumpMode)
                        {
                            case JumpMode.CallAndStop:
                            case JumpMode.CallAndResume:
                                var targetMacro = FindSystemTargetMacro(Macros, action);
                                stopAfterJump = action.JumpMode == JumpMode.CallAndStop;
                                await ExecuteMacroAsync(targetMacro, cancellationToken, callDepth + 1, triggerEvent: triggerEvent);
                                if (stopAfterJump)
                                    nextActionIndex = actionIds.Length;
                                break;
                            case JumpMode.GotoAction:
                                if (onlyActionId is not null)
                                    throw new InvalidOperationException(
                                        "Un Jump vers une action doit être exécuté avec toute sa macro.");
                                var targetAction = FindJumpTargetAction(macro, action);
                                nextActionIndex = Array.IndexOf(actionIds, targetAction.Id);
                                if (nextActionIndex < 0)
                                    throw new InvalidOperationException("L'action cible de Jump n'est plus dans la séquence.");
                                break;
                            default:
                                throw new InvalidOperationException("Le mode de l'action Jump est invalide.");
                        }
                        break;
                    case MacroActionKind.If:
                        var condition = EvaluateIf(FormatActionResult(actionContext.Result), action);
                        actionContext.Result = condition;
                        if (!condition)
                            nextActionIndex++;
                        break;
                    default:
                        throw new InvalidOperationException($"Type d'action non pris en charge : {action.Kind}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (action.Kind == MacroActionKind.Plugin ||
                                               exception is InvalidOperationException or IOException or JsonException or
                                               TimeoutException or UnauthorizedAccessException or
                                               System.ComponentModel.Win32Exception or
                                               System.Security.SecurityException)
            {
                actionContext.Log($"Erreur dans « {macro.Name} » ({action.Kind}) : {exception.Message}", LogLevel.Error);
                actionContext.Result = null;
            }

            if (stopAfterJump)
                break;
            previousActionResult = actionContext.Result;
            actionIndex = nextActionIndex;
        }
        if (!cancellationToken.IsCancellationRequested)
            WriteLog(selectedAction is null
                ? $"Macro terminée : « {macro.Name} »."
                : $"Action terminée : « {selectedAction.DisplayName} ».");
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
            Foreground = Brushes.Gray,
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
