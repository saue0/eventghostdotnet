using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using EventGhostDotNet.PluginContracts;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private async void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMacro is not MacroDefinition macro)
        {
            MessageBox.Show(this, "Créez ou sélectionnez d'abord une macro.", "Aucune macro sélectionnée",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var choice = PromptForActionKind();
        if (choice is null)
            return;

        var action = choice.Kind == MacroActionKind.Plugin
            ? ConfigurePluginAction(choice.PluginId!, choice.ActionId!, null, choice.InstanceId)
            : PromptForAction(choice.Kind, sourceMacro: macro);
        if (action is null)
            return;

        macro.Actions.Add(action);
        var startupCount = PluginStartups.Count;
        if (action.Kind == MacroActionKind.Plugin)
            EnsurePluginStartupConfiguration(action.PluginId);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectTreeItem(macro, action);
        WriteLog($"Action ajoutée à « {macro.Name} » : {action.DisplayName}.");
        if (PluginStartups.Count != startupCount && IsLoaded)
            await RestartConfiguredPluginsAsync();
    }

    private async void EditAction_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigurationTree.SelectedItem is not MacroAction action)
            return;

        var macro = Macros.FirstOrDefault(item => item.Actions.Contains(action)) ??
                    (StartupActions.Contains(action) ? _startupMacro : null);
        if (macro is null)
            return;

        var updatedAction = action.Kind == MacroActionKind.Plugin
            ? ConfigurePluginAction(action.PluginId, action.ActionId, action)
            : PromptForAction(action.Kind, action, macro);
        if (updatedAction is null)
            return;

        var actionIndex = macro.Actions.IndexOf(action);
        if (actionIndex < 0)
            return;

        if (action.Kind == MacroActionKind.Plugin)
        {
            var plugin = _pluginLoader.Find(action.PluginId);
            if (plugin is null)
            {
                MessageBox.Show(this, $"Plugin introuvable : {action.PluginId}", "Plugin indisponible",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            try
            {
                await plugin.ActionChangedAsync(ResolvePluginAction(plugin, action),
                    ResolvePluginAction(plugin, updatedAction));
            }
            catch (Exception exception)
            {
                WriteLog($"Erreur de modification du plugin {action.PluginId} : {exception.Message}", LogLevel.Error);
                MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        updatedAction.Id = action.Id;
        updatedAction.IsEnabled = action.IsEnabled;
        var startupCount = PluginStartups.Count;
        if (updatedAction.Kind == MacroActionKind.Plugin)
            EnsurePluginStartupConfiguration(updatedAction.PluginId);
        macro.Actions[actionIndex] = updatedAction;
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        if (ReferenceEquals(macro, _startupMacro))
            SelectStartupActionItem(updatedAction);
        else
            SelectTreeItem(macro, updatedAction);
        WriteLog($"Action modifiée dans « {macro.Name} » : {updatedAction.DisplayName}.");
        if (PluginStartups.Count != startupCount && IsLoaded)
            await RestartConfiguredPluginsAsync();
    }

    private MacroAction? PromptForAction(
        MacroActionKind kind, MacroAction? existing = null, MacroDefinition? sourceMacro = null)
    {
        if (kind == MacroActionKind.RaiseEvent)
            return PromptForRaiseEventAction(existing);
        if (kind == MacroActionKind.Log)
            return PromptForLogAction(existing);
        if (kind == MacroActionKind.PowerShell)
            return PromptForScript(existing);
        if (kind is MacroActionKind.Jump or MacroActionKind.If)
            return PromptForFlowAction(kind, existing, sourceMacro);
        if (IsTargetedSystemAction(kind))
            return PromptForSystemAction(kind, existing);
        if (kind == MacroActionKind.AskYesNo)
            return PromptForYesNoAction(existing);

        var isEditing = existing is not null;
        var value = kind switch
        {
            MacroActionKind.Delay => PromptForText(isEditing ? "Modifier l'action d'attente" : "Action d'attente",
                "Durée en millisecondes (0 à 600000) :", existing?.Value ?? "1000"),
            MacroActionKind.ShowMessage => PromptForText(isEditing ? "Modifier l'action de message" : "Action de message",
                "Message à afficher :", existing?.Value ?? string.Empty),
            MacroActionKind.OpenUrl => PromptForText(isEditing ? "Modifier l'action d'ouverture d'URL" : "Action d'ouverture d'URL",
                "URL HTTP ou HTTPS :", existing?.Value ?? "https://"),
            MacroActionKind.Beep => existing?.Value ?? string.Empty,
            MacroActionKind.Stop => string.Empty,
            _ => null
        };

        if (value is null)
            return null;

        if (kind == MacroActionKind.Delay &&
            (!int.TryParse(value, out var milliseconds) || milliseconds is < 0 or > 600000))
        {
            MessageBox.Show(this, "La durée doit être un nombre entier compris entre 0 et 600000.", "Durée invalide",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        if (kind == MacroActionKind.OpenUrl &&
            (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            MessageBox.Show(this, "Saisissez une URL absolue commençant par http:// ou https://.", "URL invalide",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return new MacroAction
        {
            Kind = kind,
            Value = value,
            Target = existing?.Target ?? string.Empty
        };
    }

    private MacroAction? PromptForYesNoAction(MacroAction? existing)
    {
        var dialog = new Window
        {
            Title = existing is null ? "Question Oui/Non" : "Modifier la question Oui/Non",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(16)
        };
        var panel = new StackPanel { Width = 440 };
        panel.Children.Add(new TextBlock { Text = "Question :", Margin = new Thickness(0, 0, 0, 5) });
        var question = new TextBox
        {
            Text = existing?.Value ?? "Souhaitez-vous continuer ?",
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(question);
        panel.Children.Add(new TextBlock { Text = "Titre de la fenêtre (facultatif) :", Margin = new Thickness(0, 0, 0, 5) });
        var title = new TextBox
        {
            Text = existing?.Target ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button { Content = "Enregistrer", IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(question.Text))
            {
                MessageBox.Show(dialog, "Saisissez une question.", "Question requise",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        return dialog.ShowDialog() == true
            ? new MacroAction { Kind = MacroActionKind.AskYesNo, Value = question.Text, Target = title.Text }
            : null;
    }

    private sealed record LogLevelChoice(LogLevel Level, string Label);

    private MacroAction? PromptForLogAction(MacroAction? existing)
    {
        var dialog = new Window
        {
            Title = existing is null ? "Action de journal" : "Modifier l'action de journal",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(16)
        };
        var panel = new StackPanel { Width = 440 };
        panel.Children.Add(new TextBlock { Text = "Texte à ajouter au journal :", Margin = new Thickness(0, 0, 0, 5) });
        var message = new TextBox
        {
            Text = existing?.Value ?? string.Empty,
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(message);
        panel.Children.Add(new TextBlock { Text = "Niveau :", Margin = new Thickness(0, 0, 0, 5) });
        var levels = new[]
        {
            new LogLevelChoice(LogLevel.Debug, "Debug"),
            new LogLevelChoice(LogLevel.Information, "Information"),
            new LogLevelChoice(LogLevel.Warning, "Avertissement"),
            new LogLevelChoice(LogLevel.Error, "Erreur")
        };
        var selector = new ComboBox
        {
            ItemsSource = levels,
            DisplayMemberPath = nameof(LogLevelChoice.Label),
            SelectedItem = levels.FirstOrDefault(choice => choice.Level == (existing?.LogLevel ?? LogLevel.Information)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(selector);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button { Content = "Enregistrer", IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(message.Text) || selector.SelectedItem is not LogLevelChoice)
            {
                MessageBox.Show(dialog, "Saisissez un message et sélectionnez un niveau.",
                    "Journal incomplet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        if (dialog.ShowDialog() != true || selector.SelectedItem is not LogLevelChoice selected)
            return null;
        return new MacroAction
        {
            Kind = MacroActionKind.Log,
            Value = message.Text,
            Target = existing?.Target ?? string.Empty,
            LogLevel = selected.Level == LogLevel.Information ? null : selected.Level
        };
    }

    private MacroAction? PromptForRaiseEventAction(MacroAction? existing)
    {
        var dialog = new Window
        {
            Title = existing is null ? "Déclencher un événement" : "Modifier l'événement déclenché",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(16)
        };
        var panel = new StackPanel { Width = 440 };
        panel.Children.Add(new TextBlock { Text = "Nom de l'événement :", Margin = new Thickness(0, 0, 0, 5) });
        var eventName = new TextBox
        {
            Text = existing?.Target ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(eventName);
        panel.Children.Add(new TextBlock { Text = "Payload (facultatif) :", Margin = new Thickness(0, 0, 0, 5) });
        var payload = new TextBox
        {
            Text = existing?.Value ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(payload);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button { Content = "Enregistrer", IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(eventName.Text))
            {
                MessageBox.Show(dialog, "Saisissez un nom d'événement.", "Nom requis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        if (dialog.ShowDialog() != true)
            return null;

        return new MacroAction
        {
            Kind = MacroActionKind.RaiseEvent,
            Target = eventName.Text.Trim(),
            Value = payload.Text
        };
    }

    private MacroAction? PromptForSystemAction(MacroActionKind kind, MacroAction? existing)
    {
        var dialog = new Window
        {
            Title = SystemActionName(kind),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(16)
        };
        var panel = new StackPanel { Width = 560 };
        panel.Children.Add(new TextBlock { Text = "Élément cible :", Margin = new Thickness(0, 0, 0, 5) });
        var choices = GetActivationTargetChoices();
        ActivationTargetKind? existingKind = existing?.Kind is MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem
            ? existing.ActivationTarget
            : existing is null ? null : GetActivationTargetKind(existing);
        var selector = new ComboBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(ActivationTargetChoice.Label),
            Margin = new Thickness(0, 0, 0, 12),
            SelectedItem = existing is null ? choices.FirstOrDefault() :
                choices.FirstOrDefault(choice => choice.Kind == existingKind && MatchesActivationChoice(choice, existing))
        };
        panel.Children.Add(selector);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button { Content = "Enregistrer", IsDefault = true };
        save.Click += (_, _) =>
        {
            if (selector.SelectedItem is not ActivationTargetChoice)
            {
                MessageBox.Show(dialog, "Sélectionnez un élément cible.",
                    "Cible requise", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        if (dialog.ShowDialog() != true || selector.SelectedItem is not ActivationTargetChoice selected)
            return null;

        return new MacroAction
        {
            Kind = kind is MacroActionKind.EnableMacro or MacroActionKind.EnableAction or MacroActionKind.ActivateItem
                ? MacroActionKind.ActivateItem : MacroActionKind.DeactivateItem,
            ActivationTarget = selected.Kind,
            Target = selected.Target,
            Value = selected.Value
        };
    }

    private static bool MatchesActivationChoice(ActivationTargetChoice choice, MacroAction action)
    {
        var targetMatches = choice.Kind == ActivationTargetKind.PluginStartup
            ? string.Equals(choice.Target, action.Target, StringComparison.Ordinal)
            : string.Equals(choice.Target, "Startup", StringComparison.OrdinalIgnoreCase)
                ? string.Equals(choice.Target, action.Target, StringComparison.OrdinalIgnoreCase)
                : Guid.TryParse(choice.Target, out var choiceId) &&
                  Guid.TryParse(action.Target, out var targetId) && choiceId == targetId;
        if (!targetMatches)
            return false;
        return choice.Kind switch
        {
            ActivationTargetKind.Event => string.Equals(choice.Value, action.Value, StringComparison.OrdinalIgnoreCase),
            ActivationTargetKind.Action => Guid.TryParse(choice.Value, out var choiceActionId) &&
                                           Guid.TryParse(action.Value, out var actionId) && choiceActionId == actionId,
            _ => true
        };
    }

    private List<ActivationTargetChoice> GetActivationTargetChoices()
    {
        var choices = new List<ActivationTargetChoice>();
        foreach (var group in MacroGroups)
            choices.Add(new(ActivationTargetKind.Group, group.Id.ToString(), string.Empty, $"Groupe : {group.Name}"));
        foreach (var macro in Macros)
        {
            var macroId = macro.Id.ToString();
            choices.Add(new(ActivationTargetKind.Macro, macroId, string.Empty, $"Macro : {macro.Name}"));
            foreach (var macroEvent in macro.EventItems)
                choices.Add(new(ActivationTargetKind.Event, macroId, macroEvent.Name,
                    $"Événement : {macro.Name} — {macroEvent.Name}"));
            foreach (var (action, index) in macro.Actions.Select((action, index) => (action, index)))
                choices.Add(new(ActivationTargetKind.Action, macroId, action.Id.ToString(),
                    $"Action : {macro.Name} — {index + 1}. {ActionTargetName(action)}"));
        }
        foreach (var (action, index) in StartupActions.Select((action, index) => (action, index)))
            choices.Add(new(ActivationTargetKind.Action, "Startup", action.Id.ToString(),
                $"Action : Démarrage — {index + 1}. {ActionTargetName(action)}"));
        foreach (var startup in PluginStartups)
            choices.Add(new(ActivationTargetKind.PluginStartup, startup.InstanceId, string.Empty,
                $"Démarrage de plugin : {startup.DisplayName}"));
        return choices;
    }

    private MacroAction? PromptForFlowAction(
        MacroActionKind kind, MacroAction? existing, MacroDefinition? sourceMacro)
    {
        var dialog = new Window
        {
            Title = SystemActionName(kind),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(16)
        };
        var panel = new StackPanel { Width = 480 };
        ComboBox? modeSelector = null;
        ComboBox? macroSelector = null;
        ComboBox? actionSelector = null;
        ComboBox? comparisonSelector = null;
        TextBox? comparisonValue = null;
        var targetLabel = new TextBlock { Margin = new Thickness(0, 0, 0, 5) };

        if (kind == MacroActionKind.Jump)
        {
            panel.Children.Add(new TextBlock { Text = "Type de Jump :", Margin = new Thickness(0, 0, 0, 5) });
            var modeChoices = new[]
            {
                new JumpModeChoice(JumpMode.CallAndStop, "Lancer une macro puis arrêter celle-ci"),
                new JumpModeChoice(JumpMode.CallAndResume, "Lancer une macro puis reprendre celle-ci"),
                new JumpModeChoice(JumpMode.GotoAction, "Sauter à une action de cette macro")
            };
            modeSelector = new ComboBox
            {
                ItemsSource = modeChoices,
                DisplayMemberPath = nameof(JumpModeChoice.Label),
                SelectedItem = modeChoices.First(choice => choice.Mode == (existing?.JumpMode ?? JumpMode.CallAndStop)),
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(modeSelector);

            var macroChoices = Macros.Select((macro, index) =>
                new SystemMacroChoice(macro, $"{index + 1}. {macro.Name}")).ToArray();
            macroSelector = new ComboBox
            {
                ItemsSource = macroChoices,
                DisplayMemberPath = nameof(SystemMacroChoice.Label),
                SelectedItem = existing is null ? macroChoices.FirstOrDefault() : macroChoices.FirstOrDefault(choice =>
                    Guid.TryParse(existing.Target, out var id) && choice.Macro.Id == id),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var actionChoices = sourceMacro?.Actions.Select((action, index) =>
                new SystemActionChoice(action, $"{index + 1}. {ActionTargetName(action)}")).ToArray() ?? [];
            actionSelector = new ComboBox
            {
                ItemsSource = actionChoices,
                DisplayMemberPath = nameof(SystemActionChoice.Label),
                SelectedItem = existing is null ? actionChoices.FirstOrDefault() : actionChoices.FirstOrDefault(choice =>
                    Guid.TryParse(existing.Value, out var id) && choice.Action.Id == id),
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(targetLabel);
            panel.Children.Add(macroSelector);
            panel.Children.Add(actionSelector);

            void UpdateJumpTargets()
            {
                var gotoAction = (modeSelector.SelectedItem as JumpModeChoice)?.Mode == JumpMode.GotoAction;
                targetLabel.Text = gotoAction ? "Action cible dans la macro actuelle :" : "Macro cible :";
                macroSelector.Visibility = gotoAction ? Visibility.Collapsed : Visibility.Visible;
                actionSelector.Visibility = gotoAction ? Visibility.Visible : Visibility.Collapsed;
            }

            modeSelector.SelectionChanged += (_, _) => UpdateJumpTargets();
            UpdateJumpTargets();
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Tester la valeur configurée de l’action précédente :",
                Margin = new Thickness(0, 0, 0, 5)
            });
            var comparisonChoices = new[]
            {
                new IfComparisonChoice(IfComparison.Equals, "Est égale à"),
                new IfComparisonChoice(IfComparison.Contains, "Contient"),
                new IfComparisonChoice(IfComparison.NotEquals, "Est différente de")
            };
            comparisonSelector = new ComboBox
            {
                ItemsSource = comparisonChoices,
                DisplayMemberPath = nameof(IfComparisonChoice.Label),
                SelectedItem = comparisonChoices.First(choice =>
                    choice.Comparison == (existing?.IfComparison ?? IfComparison.Equals)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            comparisonValue = new TextBox
            {
                Text = existing?.Value ?? string.Empty,
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(comparisonSelector);
            panel.Children.Add(comparisonValue);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button { Content = "Enregistrer", IsDefault = true };
        save.Click += (_, _) =>
        {
            var validJumpTarget = kind != MacroActionKind.Jump ||
                ((modeSelector?.SelectedItem as JumpModeChoice)?.Mode == JumpMode.GotoAction
                    ? actionSelector?.SelectedItem is SystemActionChoice
                    : macroSelector?.SelectedItem is SystemMacroChoice);
            if (!validJumpTarget)
            {
                MessageBox.Show(dialog, "Sélectionnez une cible valide pour l'action Jump.",
                    "Cible requise", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        if (dialog.ShowDialog() != true)
            return null;

        if (kind == MacroActionKind.If)
        {
            var comparison = (comparisonSelector?.SelectedItem as IfComparisonChoice)?.Comparison
                ?? throw new InvalidOperationException("La comparaison de l'action If n'est pas sélectionnée.");
            return new MacroAction
            {
                Kind = kind,
                IfComparison = comparison,
                Value = comparisonValue?.Text ?? string.Empty
            };
        }

        var selectedMode = (modeSelector?.SelectedItem as JumpModeChoice)?.Mode
            ?? throw new InvalidOperationException("Le mode de l'action Jump n'est pas sélectionné.");
        var selectedMacro = macroSelector?.SelectedItem as SystemMacroChoice;
        var selectedAction = actionSelector?.SelectedItem as SystemActionChoice;
        return new MacroAction
        {
            Kind = kind,
            JumpMode = selectedMode,
            Target = selectedMode == JumpMode.GotoAction ? string.Empty : selectedMacro!.Macro.Id.ToString(),
            Value = selectedMode == JumpMode.GotoAction ? selectedAction!.Action.Id.ToString() : string.Empty
        };
    }

    private static bool IsSystemAction(MacroActionKind kind) =>
        kind is MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
            MacroActionKind.EnableAction or MacroActionKind.DisableAction or
            MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem or
            MacroActionKind.Jump or MacroActionKind.If;

    private static bool IsTargetedSystemAction(MacroActionKind kind) =>
        kind is MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
            MacroActionKind.EnableAction or MacroActionKind.DisableAction or
            MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem;

    private static string SystemActionName(MacroActionKind kind) => kind switch
    {
        MacroActionKind.EnableMacro => "Système — activer une macro",
        MacroActionKind.DisableMacro => "Système — désactiver une macro",
        MacroActionKind.EnableAction => "Système — activer une action",
        MacroActionKind.DisableAction => "Système — désactiver une action",
        MacroActionKind.ActivateItem => "Système — activer",
        MacroActionKind.DeactivateItem => "Système — désactiver",
        MacroActionKind.Jump => "Système — Jump",
        MacroActionKind.If => "Système — If",
        _ => throw new InvalidOperationException($"Action système inconnue : {kind}")
    };

    private static string ActionTargetName(MacroAction action) =>
        IsSystemAction(action.Kind) ? SystemActionName(action.Kind) : action.DisplayName;

    private static MacroDefinition FindSystemTargetMacro(IEnumerable<MacroDefinition> macros, MacroAction action)
    {
        if (!Guid.TryParse(action.Target, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Identifiant de macro cible invalide.");
        var matches = macros.Where(macro => macro.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException("La macro cible est introuvable ou son identifiant est dupliqué.");
    }

    private static MacroAction FindJumpTargetAction(MacroDefinition macro, MacroAction action)
    {
        if (!Guid.TryParse(action.Value, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Identifiant d'action Jump invalide.");
        var matches = macro.Actions.Where(item => item.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException($"L'action cible de Jump dans « {macro.Name} » est introuvable ou son identifiant est dupliqué.");
    }

    private static ActivationTargetKind GetActivationTargetKind(MacroAction action) => action.Kind switch
    {
        MacroActionKind.EnableMacro or MacroActionKind.DisableMacro => ActivationTargetKind.Macro,
        MacroActionKind.EnableAction or MacroActionKind.DisableAction => ActivationTargetKind.Action,
        MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem => action.ActivationTarget
            ?? throw new InvalidOperationException("Le type de l'élément cible est absent."),
        _ => throw new InvalidOperationException($"Action système inconnue : {action.Kind}")
    };

    private static ConfigurationItem? FindActivationTarget(
        IEnumerable<MacroGroup> groups, IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroAction> startupActions, IEnumerable<PluginStartupConfiguration> startups,
        ActivationTargetKind kind, string target, string value)
    {
        switch (kind)
        {
            case ActivationTargetKind.Group:
                return Guid.TryParse(target, out var groupId)
                    ? groups.SingleOrDefault(group => group.Id == groupId) : null;
            case ActivationTargetKind.Macro:
                return Guid.TryParse(target, out var macroId)
                    ? macros.SingleOrDefault(macro => macro.Id == macroId) : null;
            case ActivationTargetKind.Event:
                if (!Guid.TryParse(target, out var eventMacroId))
                    return null;
                var eventMacro = macros.SingleOrDefault(macro => macro.Id == eventMacroId);
                return eventMacro?.EventItems.SingleOrDefault(item =>
                    string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
            case ActivationTargetKind.Action:
                if (!Guid.TryParse(value, out var actionId))
                    return null;
                var actions = string.Equals(target, "Startup", StringComparison.OrdinalIgnoreCase)
                    ? startupActions
                    : Guid.TryParse(target, out var actionMacroId)
                        ? macros.SingleOrDefault(macro => macro.Id == actionMacroId)?.Actions
                        : null;
                return actions?.SingleOrDefault(item => item.Id == actionId);
            case ActivationTargetKind.PluginStartup:
                return startups.SingleOrDefault(item => string.Equals(item.InstanceId, target, StringComparison.Ordinal));
            default:
                throw new InvalidOperationException($"Type d'élément cible inconnu : {kind}");
        }
    }

    private static bool GetActivationState(ConfigurationItem item) => item switch
    {
        MacroGroup group => group.IsEnabled,
        MacroDefinition macro => macro.IsEnabled,
        MacroEvent macroEvent => macroEvent.IsEnabled,
        MacroAction action => action.IsEnabled,
        PluginStartupConfiguration startup => startup.IsEnabled,
        _ => throw new InvalidOperationException("Cet élément ne peut pas être activé ou désactivé.")
    };

    private static void SetActivationState(ConfigurationItem item, bool enabled)
    {
        switch (item)
        {
            case MacroGroup group: group.IsEnabled = enabled; break;
            case MacroDefinition macro: macro.IsEnabled = enabled; break;
            case MacroEvent macroEvent: macroEvent.IsEnabled = enabled; break;
            case MacroAction action: action.IsEnabled = enabled; break;
            case PluginStartupConfiguration startup: startup.IsEnabled = enabled; break;
            default: throw new InvalidOperationException("Cet élément ne peut pas être activé ou désactivé.");
        }
    }

    private static string ActivationTargetName(ConfigurationItem item) => item switch
    {
        MacroGroup group => $"Groupe « {group.Name} »",
        MacroDefinition macro => $"Macro « {macro.Name} »",
        MacroEvent macroEvent => $"Événement « {macroEvent.Name} » de « {macroEvent.Macro.Name} »",
        MacroAction action => $"Action « {ActionTargetName(action)} »",
        PluginStartupConfiguration startup => $"Démarrage « {startup.DisplayName} »",
        _ => throw new InvalidOperationException("Cet élément ne peut pas être activé ou désactivé.")
    };

    private static SystemActionResult ExecuteSystemAction(
        IEnumerable<MacroGroup> groups, IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroAction> startupActions, IEnumerable<PluginStartupConfiguration> startups, MacroAction action)
    {
        var kind = GetActivationTargetKind(action);
        var target = FindActivationTarget(groups, macros, startupActions, startups, kind, action.Target, action.Value)
            ?? throw new InvalidOperationException("L'élément cible est introuvable.");
        var previous = GetActivationState(target);
        var enabled = action.Kind is MacroActionKind.EnableMacro or MacroActionKind.EnableAction or MacroActionKind.ActivateItem;
        SetActivationState(target, enabled);
        return new SystemActionResult($"{ActivationTargetName(target)} : {(enabled ? "activé" : "désactivé")}.",
            kind, action.Target, action.Value, previous, enabled);
    }

    private static string AcceptRuntimeStateChange(string cleanConfiguration, SystemActionResult result)
    {
        if (result.PreviousState == result.CurrentState)
            return cleanConfiguration;

        var configuration = JsonSerializer.Deserialize<ConfigurationDocument>(cleanConfiguration, JsonOptions)
            ?? throw new JsonException("La référence de configuration est invalide.");
        var target = FindActivationTarget(configuration.MacroGroups, configuration.Macros,
            configuration.StartupActions, configuration.PluginStartups, result.Kind, result.Target, result.Value);
        if (target is null || GetActivationState(target) != result.PreviousState)
            return cleanConfiguration;

        // Preserve a manually changed state in the dirty comparison.
        SetActivationState(target, result.CurrentState);
        return JsonSerializer.Serialize(configuration, JsonOptions);
    }

    private void RefreshSystemActionDescriptions(IEnumerable<MacroDefinition> macros) =>
        RefreshSystemActionDescriptions(macros, MacroGroups, StartupActions, PluginStartups);

    private static void RefreshSystemActionDescriptions(IEnumerable<MacroDefinition> macros,
        IEnumerable<MacroGroup> groups, IEnumerable<MacroAction> startupActions,
        IEnumerable<PluginStartupConfiguration> startups)
    {
        foreach (var (ownerMacro, action) in macros.SelectMany(macro =>
                     macro.Actions.Select(action => (Owner: macro, Action: action))))
        {
            if (!IsSystemAction(action.Kind))
                continue;
            try
            {
                if (action.Kind == MacroActionKind.If)
                {
                    var comparison = action.IfComparison switch
                    {
                        IfComparison.Equals => "est égale à",
                        IfComparison.Contains => "contient",
                        IfComparison.NotEquals => "est différente de",
                        _ => "a une comparaison invalide"
                    };
                    action.SystemDisplayName = $"{SystemActionName(action.Kind)} — valeur précédente {comparison} « {action.Value} »";
                    continue;
                }

                if (action.Kind == MacroActionKind.Jump)
                {
                    if (action.JumpMode == JumpMode.GotoAction)
                    {
                        var targetAction = FindJumpTargetAction(ownerMacro, action);
                        action.SystemDisplayName =
                            $"{SystemActionName(action.Kind)} — action {ownerMacro.Actions.IndexOf(targetAction) + 1} de « {ownerMacro.Name} »";
                    }
                    else
                    {
                        var targetMacro = FindSystemTargetMacro(macros, action);
                        var behavior = action.JumpMode switch
                        {
                            JumpMode.CallAndStop => "appeler et arrêter",
                            JumpMode.CallAndResume => "appeler puis reprendre",
                            _ => "mode invalide"
                        };
                        action.SystemDisplayName = $"{SystemActionName(action.Kind)} — {behavior} « {targetMacro.Name} »";
                    }
                    continue;
                }

                var target = FindActivationTarget(groups, macros, startupActions, startups,
                    GetActivationTargetKind(action), action.Target, action.Value)
                    ?? throw new InvalidOperationException("L'élément cible est introuvable.");
                action.SystemDisplayName = $"{SystemActionName(action.Kind)} : {ActivationTargetName(target)}";
            }
            catch (InvalidOperationException)
            {
                action.SystemDisplayName = $"{SystemActionName(action.Kind)} : cible introuvable";
            }
        }
    }

    private void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigurationTree.SelectedItem is not MacroAction action)
            return;

        var macro = Macros.FirstOrDefault(item => item.Actions.Contains(action)) ??
                    (StartupActions.Contains(action) ? _startupMacro : null);
        if (macro is null)
            return;

        if (MessageBox.Show(this, $"Supprimer l'action « {action.DisplayName} » de « {macro.Name} » ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        macro.Actions.Remove(action);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        if (ReferenceEquals(macro, _startupMacro))
            SelectStartupSection();
        else
            SelectTreeItem(macro);
        WriteLog($"Action supprimée de « {macro.Name} ».");
    }

    private MacroAction? ConfigurePluginAction(
        string pluginId, string actionId, MacroAction? existing, string? instanceId = null)
    {
        var plugin = _pluginLoader.Find(pluginId);
        if (plugin is null)
        {
            MessageBox.Show(this, $"Plugin introuvable : {pluginId}", "Plugin indisponible",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        try
        {
            var host = new PluginHost(this, new ActionContext(this, _sessionCancellation.Token, null));
            var provider = plugin as IPluginInstanceProvider;
            instanceId = existing?.InstanceId ?? instanceId;
            PluginActionData? data;
            if (provider is not null && provider.RequiresInstance(actionId))
            {
                if (string.IsNullOrEmpty(instanceId))
                    throw new InvalidOperationException("Ajoutez d'abord une configuration de démarrage pour ce plugin.");
                var configuration = FindInstanceConfiguration(plugin, provider, instanceId);
                data = provider.ConfigureInstanceAction(actionId,
                    existing is null ? null : ToPluginData(existing), ToPluginData(configuration), host);
            }
            else
            {
                data = plugin.ConfigureAction(actionId, existing is null ? null : ToPluginData(existing), host);
            }
            if (data is null)
                return null;
            if (!string.Equals(data.ActionId, actionId, StringComparison.Ordinal))
                throw new InvalidOperationException("Le plugin a retourné un type d'action inattendu.");
            return new MacroAction
            {
                Kind = MacroActionKind.Plugin,
                PluginId = plugin.Id,
                ActionId = data.ActionId,
                Value = data.Value,
                Target = data.Target,
                InstanceId = instanceId,
                PluginDisplayName = plugin.DescribeAction(data)
            };
        }
        catch (Exception exception)
        {
            WriteLog($"Erreur de configuration du plugin {pluginId} : {exception.Message}", LogLevel.Error);
            MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private PluginActionData ResolvePluginAction(IPlugin plugin, MacroAction action)
    {
        var data = ToPluginData(action);
        if (plugin is IPluginInstanceProvider provider && provider.RequiresInstance(action.ActionId) &&
            !string.IsNullOrEmpty(action.InstanceId))
        {
            var configuration = FindInstanceConfiguration(plugin, provider, action.InstanceId);
            return provider.BindInstance(data, ToPluginData(configuration));
        }
        return data;
    }

    private void EnsurePluginActionEnabled(IPlugin plugin, MacroAction action)
    {
        if (plugin is IPluginInstanceProvider provider && provider.RequiresInstance(action.ActionId) &&
            !string.IsNullOrEmpty(action.InstanceId))
        {
            if (!FindInstanceConfiguration(plugin, provider, action.InstanceId).IsEnabled)
                throw new InvalidOperationException($"Le démarrage associé au plugin {plugin.Name} est désactivé.");
            return;
        }

        var configurations = PluginStartups.Where(configuration =>
            string.Equals(configuration.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (configurations.Length > 0 && configurations.All(configuration => !configuration.IsEnabled))
            throw new InvalidOperationException($"Le plugin {plugin.Name} est désactivé.");
    }

    private void RefreshPluginDescriptions(IEnumerable<MacroDefinition> macros)
    {
        foreach (var action in macros.SelectMany(macro => macro.Actions).Concat(StartupActions)
                     .Where(action => action.Kind == MacroActionKind.Plugin))
        {
            var plugin = _pluginLoader.Find(action.PluginId);
            if (plugin is null)
                continue;
            try
            {
                action.PluginDisplayName = plugin.DescribeAction(ResolvePluginAction(plugin, action));
                if (plugin is IPluginInstanceProvider provider && provider.RequiresInstance(action.ActionId) &&
                    !string.IsNullOrEmpty(action.InstanceId))
                {
                    var configuration = FindInstanceConfiguration(plugin, provider, action.InstanceId);
                    action.PluginDisplayName += $" ({provider.GetInstanceName(ToPluginData(configuration))})";
                }
            }
            catch (Exception exception)
            {
                action.PluginDisplayName = $"{action.PluginId} — {action.ActionId} : configuration indisponible";
                WriteLog($"Impossible d'afficher l'action {action.PluginId}/{action.ActionId} : {exception.Message}", LogLevel.Error);
            }
        }
    }

    internal void EditActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroAction action })
        {
            var macro = Macros.FirstOrDefault(item => item.Actions.Contains(action));
            if (macro is not null)
                SelectTreeItem(macro, action);
            else if (StartupActions.Contains(action))
                SelectStartupActionItem(action);
            EditAction_Click(sender, e);
        }
    }

    internal void RemoveActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroAction action })
        {
            var macro = Macros.FirstOrDefault(item => item.Actions.Contains(action));
            if (macro is not null)
                SelectTreeItem(macro, action);
            else if (StartupActions.Contains(action))
                SelectStartupActionItem(action);
            RemoveAction_Click(sender, e);
        }
    }

    private static bool EvaluateIf(string? previousResult, MacroAction action)
    {
        if (previousResult is null)
            return false;
        return action.IfComparison switch
        {
            IfComparison.Equals => string.Equals(previousResult, action.Value, StringComparison.OrdinalIgnoreCase),
            IfComparison.Contains => previousResult.Contains(action.Value, StringComparison.OrdinalIgnoreCase),
            IfComparison.NotEquals => !string.Equals(previousResult, action.Value, StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException("La comparaison de l'action If est invalide.")
        };
    }

    private static string? FormatActionResult(object? result) => result switch
    {
        null => null,
        string value => value,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement element => element.GetRawText(),
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        _ => result.ToString()
    };

    private MacroAction? PromptForScript(MacroAction? existing)
    {
        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            MinWidth = 640,
            MinHeight = 300,
            Text = existing?.Value ?? "# Exemple : écrire dans le journal\nWrite-Output 'Bonjour depuis PowerShell'"
        };
        editor.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Tab)
            {
                editor.SelectedText = "    ";
                args.Handled = true;
            }
        };

        var dialog = new Window
        {
            Title = "Script PowerShell",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 760,
            Height = 500,
            MinWidth = 680,
            MinHeight = 450
        };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var warning = new TextBlock
        {
            Text = "Le script s'exécute avec les droits de l'application. N'utilisez que du code de confiance.",
            Foreground = Brushes.DarkRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(warning, Dock.Top);
        panel.Children.Add(warning);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = new Button { Content = "Annuler", IsCancel = true };
        var add = new Button
        {
            Content = existing is null ? "Ajouter le script" : "Enregistrer le script",
            IsDefault = true
        };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(editor.Text))
            {
                MessageBox.Show(dialog, "Le script ne peut pas être vide.", "Script vide",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        panel.Children.Add(buttons);
        var unlimitedExecution = new CheckBox
        {
            Content = "Autoriser une exécution sans limite de temps (sinon 30 secondes)",
            IsChecked = existing?.UnlimitedExecution ?? false,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(unlimitedExecution, Dock.Bottom);
        panel.Children.Add(unlimitedExecution);
        var useWindowsPowerShell = new CheckBox
        {
            Content = "Utiliser Windows PowerShell 5.1 (compatibilité, au lieu de PowerShell 7)",
            IsChecked = existing?.UseWindowsPowerShell ?? false,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(useWindowsPowerShell, Dock.Bottom);
        panel.Children.Add(useWindowsPowerShell);
        panel.Children.Add(editor);
        dialog.Content = panel;

        return dialog.ShowDialog() == true
            ? new MacroAction
            {
                Kind = MacroActionKind.PowerShell,
                Value = editor.Text,
                Target = existing?.Target ?? string.Empty,
                UnlimitedExecution = unlimitedExecution.IsChecked == true,
                UseWindowsPowerShell = useWindowsPowerShell.IsChecked == true
            }
            : null;
    }

    private static async Task RunPowerShellAsync(
        string script, bool unlimitedExecution, bool useWindowsPowerShell,
        ActionContext context, CancellationToken cancellationToken)
    {
        var executable = useWindowsPowerShell
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe")
            : "pwsh.exe";
        var markerPrefix = $"__EGDN_{Guid.NewGuid():N}_";
        var logMarker = $"{markerPrefix}LOG:";
        var eventMarker = $"{markerPrefix}EVENT:";
        var resultMarker = $"{markerPrefix}RESULT:";
        var requestMarker = $"{markerPrefix}REQUEST:";
        var triggerEvent = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(context.Event)));
        var initialResult = context.Result is string initialValue
            ? $"[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(initialValue))}'))"
            : "$null";
        var wrappedScript = $$"""
            $__egEventJson = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{triggerEvent}}'))
            $eg = [pscustomobject]@{ Result = {{initialResult}}; Event = (ConvertFrom-Json -InputObject $__egEventJson) }
            $eg | Add-Member -MemberType ScriptMethod -Name Log -Value {
                param([object]$Message, [ValidateSet('Debug', 'Information', 'Warning', 'Error')][string]$Level = 'Information')
                $json = ConvertTo-Json -InputObject @{ Message = [string]$Message; Level = $Level } -Compress
                $payload = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
                [Console]::Out.WriteLine('{{logMarker}}' + $payload)
            }
            $eg | Add-Member -MemberType ScriptMethod -Name SetResult -Value {
                param([object]$Value)
                $this.Result = $Value
            }
            $eg | Add-Member -MemberType ScriptMethod -Name RaiseEvent -Value {
                param([string]$Name, [AllowNull()][string]$Payload)
                $json = ConvertTo-Json -InputObject @{ Name = $Name; Payload = $Payload } -Compress
                $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
                [Console]::Out.WriteLine('{{eventMarker}}' + $encoded)
            }
            $eg | Add-Member -MemberType ScriptMethod -Name InvokeHost -Value {
                param([string]$Operation, [string]$Name, [AllowNull()][object]$Value, [AllowNull()][object]$Key = $null)
                $json = ConvertTo-Json -InputObject @{ Operation = $Operation; Name = $Name; Value = $Value; Key = $Key } -Compress -Depth 100 -ErrorAction Stop
                $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
                [Console]::Out.WriteLine('{{requestMarker}}' + $encoded)
                $reply = [Console]::In.ReadLine()
                if ($null -eq $reply) { throw "La connexion au contexte EventGhost est fermée." }
                $responseJson = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($reply))
                $response = ConvertFrom-Json -InputObject $responseJson -ErrorAction Stop
                if (-not $response.Ok) { throw $response.Error }
                return ,$response.Value
            }
            $eg | Add-Member -MemberType ScriptMethod -Name GetVariable -Value {
                param([string]$Name)
                return ,($this.InvokeHost('Get', $Name, $null))
            }
            $eg | Add-Member -MemberType ScriptMethod -Name HasVariable -Value {
                param([string]$Name)
                return $this.InvokeHost('Has', $Name, $null)
            }
            $eg | Add-Member -MemberType ScriptMethod -Name SetVariable -Value {
                param([string]$Name, [AllowNull()][object]$Value)
                $null = $this.InvokeHost('Set', $Name, $Value)
            }
            $eg | Add-Member -MemberType ScriptMethod -Name ExchangeVariable -Value {
                param([string]$Name, [AllowNull()][object]$Value)
                return ,($this.InvokeHost('Exchange', $Name, $Value))
            }
            $eg | Add-Member -MemberType ScriptMethod -Name RemoveVariable -Value {
                param([string]$Name)
                return $this.InvokeHost('Remove', $Name, $null)
            }
            $eg | Add-Member -MemberType ScriptMethod -Name Notify -Value {
                param([string]$Title, [string]$Message, [AllowNull()][object]$Key = $null)
                if ($null -ne $Key -and $Key -isnot [string]) {
                    throw "La clé de notification doit être une chaîne."
                }
                $null = $this.InvokeHost('Notify', $Title, $Message, $Key)
            }
            try {
            {{script}}
            } finally {
                if ($null -eq $eg.Result) {
                    $__egResultJson = 'null'
                } else {
                    $__egResultJson = ConvertTo-Json -InputObject $eg.Result -Compress -Depth 100
                }
                $__egResultPayload = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$__egResultJson))
                [Console]::Out.WriteLine('{{resultMarker}}' + $__egResultPayload)
            }
            """;
        var scriptPath = Path.Combine(Path.GetTempPath(), $"EventGhostDotNet-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(scriptPath, wrappedScript, new System.Text.UTF8Encoding(true));
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            var escapedScriptPath = scriptPath.Replace("'", "''", StringComparison.Ordinal);
            startInfo.ArgumentList.Add(
                "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
                $"try {{ & '{escapedScriptPath}'; " +
                "if (-not $?) { if ($null -eq $LASTEXITCODE -or $LASTEXITCODE -eq 0) { exit 1 }; exit $LASTEXITCODE } " +
                "} catch { Write-Error -ErrorRecord $_ -ErrorAction Continue; exit 1 }");

            Process? startedProcess;
            try
            {
                startedProcess = Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception exception) when (
                !useWindowsPowerShell && exception.NativeErrorCode is 2 or 3)
            {
                throw new InvalidOperationException(
                    "PowerShell 7 est introuvable. Installez-le et ajoutez pwsh.exe au PATH, " +
                    "ou sélectionnez Windows PowerShell 5.1 dans l'action.", exception);
            }
            using var process = startedProcess
                ?? throw new InvalidOperationException("Impossible de démarrer PowerShell.");
            var outputTask = ReadPowerShellOutputAsync(
                process.StandardOutput, process.StandardInput, context, logMarker, eventMarker, resultMarker, requestMarker);
            var errorTask = process.StandardError.ReadToEndAsync();

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!unlimitedExecution)
                linkedCancellation.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var exitTask = process.WaitForExitAsync(linkedCancellation.Token);
                if (await Task.WhenAny(exitTask, outputTask) == outputTask && outputTask.IsFaulted)
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    await outputTask;
                }
                await exitTask;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                try
                {
                    await outputTask;
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
                {
                    context.Log($"Communication PowerShell interrompue : {exception.Message}", LogLevel.Warning);
                }
                _ = await errorTask;
                if (cancellationToken.IsCancellationRequested)
                    throw;
                throw new TimeoutException("Le script PowerShell a dépassé la limite de 30 secondes.");
            }

            await outputTask;
            var errors = await errorTask;
            if (!string.IsNullOrWhiteSpace(errors))
                foreach (var line in errors.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))
                    context.Log($"PowerShell stderr : {line}");
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"PowerShell s'est terminé avec le code {process.ExitCode}.");
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    private static async Task ReadPowerShellOutputAsync(
        StreamReader output, StreamWriter input, ActionContext context,
        string logMarker, string eventMarker, string resultMarker, string requestMarker)
    {
        while (await output.ReadLineAsync() is { } line)
        {
            if (line.StartsWith(logMarker, StringComparison.Ordinal))
            {
                using var logJson = JsonDocument.Parse(DecodePowerShellPayload(line[logMarker.Length..]));
                var message = logJson.RootElement.GetProperty("Message").GetString()
                    ?? throw new JsonException("Le message de journal PowerShell est invalide.");
                var levelName = logJson.RootElement.GetProperty("Level").GetString();
                if (!Enum.GetNames<LogLevel>().Contains(levelName, StringComparer.OrdinalIgnoreCase) ||
                    !Enum.TryParse<LogLevel>(levelName, true, out var level))
                    throw new JsonException("Le niveau de journal PowerShell est invalide.");
                context.Log(message, level);
            }
            else if (line.StartsWith(eventMarker, StringComparison.Ordinal))
            {
                using var eventJson = JsonDocument.Parse(DecodePowerShellPayload(line[eventMarker.Length..]));
                var name = eventJson.RootElement.GetProperty("Name").GetString();
                var payload = eventJson.RootElement.GetProperty("Payload");
                if (string.IsNullOrWhiteSpace(name))
                    throw new JsonException("Le script a tenté d'émettre un événement sans nom.");
                context.RaiseEvent(name, payload.ValueKind == JsonValueKind.Null ? null : payload.GetString());
            }
            else if (line.StartsWith(resultMarker, StringComparison.Ordinal))
            {
                using var resultJson = JsonDocument.Parse(DecodePowerShellPayload(line[resultMarker.Length..]));
                context.Result = resultJson.RootElement.ValueKind == JsonValueKind.Null
                    ? null
                    : resultJson.RootElement.Clone();
            }
            else if (line.StartsWith(requestMarker, StringComparison.Ordinal))
            {
                object response;
                try
                {
                    using var request = JsonDocument.Parse(DecodePowerShellPayload(line[requestMarker.Length..]));
                    var value = await context.Window.ExecuteScriptRequestAsync(context, request.RootElement);
                    response = new { Ok = true, Value = value, Error = (string?)null };
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                                                   ArgumentException or KeyNotFoundException)
                {
                    response = new { Ok = false, Value = (object?)null, Error = exception.Message };
                }
                var json = JsonSerializer.Serialize(response);
                var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
                await input.WriteLineAsync(encoded);
                await input.FlushAsync();
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                context.Log($"PowerShell : {line}");
            }
        }
    }

    private static string DecodePowerShellPayload(string payload)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Le protocole de communication du script PowerShell est invalide.", exception);
        }
    }

    private ActionChoice? PromptForActionKind()
    {
        var dialog = new Window
        {
            Title = "Ajouter une action",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Padding = new Thickness(18)
        };
        var panel = new StackPanel { Width = 300 };
        panel.Children.Add(new TextBlock { Text = "Type d'action :", Margin = new Thickness(0, 0, 0, 8) });
        var choices = new List<ActionChoice>
        {
            new(MacroActionKind.Log, "Journaliser"),
            new(MacroActionKind.Delay, "Attendre"),
            new(MacroActionKind.Beep, "Signal sonore"),
            new(MacroActionKind.ShowMessage, "Afficher un message"),
            new(MacroActionKind.AskYesNo, "Question Oui/Non"),
            new(MacroActionKind.Stop, "Arrêter la macro"),
            new(MacroActionKind.OpenUrl, "Ouvrir une URL"),
            new(MacroActionKind.PowerShell, "Script PowerShell"),
            new(MacroActionKind.ActivateItem, "Activer un élément"),
            new(MacroActionKind.DeactivateItem, "Désactiver un élément"),
            new(MacroActionKind.Jump, "Jump"),
            new(MacroActionKind.If, "If"),
            new(MacroActionKind.RaiseEvent, "Déclencher un événement")
        };
        choices.AddRange(CreatePluginActionChoices(_pluginLoader.Plugins, PluginStartups));
        choices = choices
            .OrderBy(choice => choice.Source switch
            {
                "Système" => 0,
                "Logitech" => 1,
                "Display" => 2,
                _ => 3
            })
            .ThenBy(choice => choice.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var actionChoicesView = CollectionViewSource.GetDefaultView(choices);
        actionChoicesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionChoice.Source)));
        var selector = new ComboBox
        {
            ItemsSource = actionChoicesView,
            SelectedIndex = 0,
            Padding = new Thickness(6)
        };
        selector.DisplayMemberPath = nameof(ActionChoice.Name);
        selector.GroupStyle.Add(new GroupStyle
        {
            HeaderTemplate = (DataTemplate)FindResource("ActionChoiceGroupHeaderTemplate")
        });
        panel.Children.Add(selector);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Annuler", IsCancel = true };
        var add = new Button { Content = "Ajouter", IsDefault = true };
        add.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        return dialog.ShowDialog() == true && selector.SelectedItem is ActionChoice choice
            ? choice
            : null;
    }

    private static IEnumerable<ActionChoice> CreatePluginActionChoices(
        IEnumerable<IPlugin> plugins, IEnumerable<PluginStartupConfiguration> startupConfigurations)
    {
        var choices = new List<ActionChoice>();
        foreach (var plugin in plugins)
        {
            var provider = plugin as IPluginInstanceProvider;
            foreach (var action in plugin.Actions)
            {
                if (provider is null || !provider.RequiresInstance(action.Id))
                {
                    choices.Add(new ActionChoice(MacroActionKind.Plugin, action.Name, plugin.Id, action.Id));
                    continue;
                }

                var configurations = startupConfigurations
                    .Where(config => string.Equals(config.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase) &&
                        provider.IsInstanceConfiguration(config.ConfigurationId) &&
                        !string.IsNullOrEmpty(config.InstanceId))
                    .ToArray();
                foreach (var configuration in configurations)
                {
                    var name = provider.GetInstanceName(ToPluginData(configuration));
                    if (configurations.Count(other => provider.GetInstanceName(ToPluginData(other)) == name) > 1)
                        name += $" — {configuration.Target} [{configuration.InstanceId}]";
                    choices.Add(new ActionChoice(MacroActionKind.Plugin, $"{action.Name} ({name})",
                        plugin.Id, action.Id, configuration.InstanceId));
                }
            }
        }
        return choices;
    }
}
