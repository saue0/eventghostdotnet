using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.VisualBasic;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet;

public partial class MainWindow : Window
{
    private const string ApplicationStartupEventName = "EGDN.Startup";
    private const string ConfigurationLoadedEventName = "EGDN.ConfigurationLoaded";
    private const int MaximumMacroCallDepth = 32;
    private const int MaximumActionExecutionsPerMacro = 10000;

    private sealed record ActionChoice(
        MacroActionKind Kind, string Name, string? PluginId = null, string? ActionId = null, string? InstanceId = null);

    private sealed record EventLogEntry(string Text, string EventName)
    {
        public override string ToString() => Text;
    }

    private sealed record SystemMacroChoice(MacroDefinition Macro, string Label);
    private sealed record SystemActionChoice(MacroAction Action, string Label);
    private sealed record JumpModeChoice(JumpMode Mode, string Label);
    private sealed record IfComparisonChoice(IfComparison Comparison, string Label);
    private sealed record SystemActionResult(
        string Message, Guid MacroId, Guid? ActionId, bool PreviousState, bool CurrentState);

    private sealed class ConfigurationDocument
    {
        public ObservableCollection<PluginStartupConfiguration> PluginStartups { get; set; } = [];
        public ObservableCollection<MacroDefinition> Macros { get; set; } = [];
    }

    private sealed class ActionContext(
        MainWindow window, CancellationToken sessionToken, object? initialResult) : IActionContext
    {
        private readonly object _resultLock = new();
        private object? _result = initialResult;

        public object? Result
        {
            get
            {
                lock (_resultLock)
                    return _result;
            }
            set
            {
                lock (_resultLock)
                    _result = value;
            }
        }

        public void Log(string message) =>
            window.Dispatcher.BeginInvoke(new Action(() => window.WriteLog(message)));

        public void RaiseEvent(string name, string? payload) => window.Dispatcher.BeginInvoke(
            new Action(() => _ = window.DispatchEventAsync(name, payload, sessionToken)));
    }

    private sealed class PluginHost(MainWindow window, ActionContext context) : IPluginHost
    {
        public Window Owner => window;
        public object? Result
        {
            get => context.Result;
            set => context.Result = value;
        }

        public IReadOnlyList<PluginActionData> GetConfiguredActions(string pluginId) =>
            window.Macros.SelectMany(macro => macro.Actions)
                .Where(action => string.Equals(action.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                .Select(action => new PluginActionData(action.ActionId, action.Value, action.Target)).ToArray();
        public IReadOnlyList<PluginStartupData> GetPluginStartupConfigurations(string pluginId) =>
            window.PluginStartups.Where(configuration =>
                    string.Equals(configuration.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                .Select(ToPluginStartupData).ToArray();
        public void Log(string message) => context.Log(message);
        public void RaiseEvent(string name, string? payload) => context.RaiseEvent(name, payload);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _configurationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EventGhostDotNet",
        "configuration.json");
    private readonly PluginLoader _pluginLoader = new();
    private readonly HashSet<Task> _activeMacroExecutions = [];
    private CancellationTokenSource _sessionCancellation = new();
    private string _cleanConfiguration = string.Empty;
    private MacroDefinition? _selectedMacro;
    private readonly StartupSection _startupSection;
    private readonly CompositeCollection _treeRoots = [];

    public ObservableCollection<MacroDefinition> Macros { get; } = [];
    public ObservableCollection<PluginStartupConfiguration> PluginStartups { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        ConfigurationPathText.Text = _configurationPath;
        _startupSection = new StartupSection(PluginStartups);
        _treeRoots.Add(_startupSection);
        _treeRoots.Add(new CollectionContainer { Collection = Macros });
        ConfigurationTree.ItemsSource = _treeRoots;
        _pluginLoader.Load(Path.Combine(AppContext.BaseDirectory, "Plugins"), WriteLog);
        LoadConfiguration();
        _cleanConfiguration = SerializeConfiguration();
    }

    private void AddMacro_Click(object sender, RoutedEventArgs e)
    {
        var details = PromptForMacroDetails(
            new MacroDefinition { Name = "Nouvelle macro", EventNames = ["Utilisateur.Demo"] }, isCreating: true);
        if (details is null)
            return;

        var macro = new MacroDefinition { Name = details.Value.Name };
        macro.SetEventNames(details.Value.EventNames);
        Macros.Add(macro);
        SelectTreeItem(macro);
        WriteLog($"Macro créée : {macro.Name} (événements : {DescribeMacroEvents(macro)}).");
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

        Macros.Remove(macro);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros);
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
        RefreshSystemActionDescriptions(Macros);
        SelectTreeItem(macro);
        WriteLog($"Macro modifiée : {macro.Name} (événements : {DescribeMacroEvents(macro)}).");
    }

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
        RefreshSystemActionDescriptions(Macros);
        SelectTreeItem(macro, action);
        WriteLog($"Action ajoutée à « {macro.Name} » : {action.DisplayName}.");
        if (PluginStartups.Count != startupCount && IsLoaded)
            await RestartConfiguredPluginsAsync();
    }

    private async void EditAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMacro is not MacroDefinition macro ||
            ConfigurationTree.SelectedItem is not MacroAction action)
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
                WriteLog($"Erreur de modification du plugin {action.PluginId} : {exception.Message}");
                MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        updatedAction.Id = action.Id;
        updatedAction.IsEnabled = action.IsEnabled;
        macro.Actions[actionIndex] = updatedAction;
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros);
        SelectTreeItem(macro, updatedAction);
        EditActionButton.IsEnabled = updatedAction.Kind != MacroActionKind.Beep;
        WriteLog($"Action modifiée dans « {macro.Name} » : {updatedAction.DisplayName}.");
    }

    private MacroAction? PromptForAction(
        MacroActionKind kind, MacroAction? existing = null, MacroDefinition? sourceMacro = null)
    {
        if (kind is MacroActionKind.Jump or MacroActionKind.If)
            return PromptForFlowAction(kind, existing, sourceMacro);
        if (IsTargetedSystemAction(kind))
            return PromptForSystemAction(kind, existing);

        var isEditing = existing is not null;
        var value = kind switch
        {
            MacroActionKind.Log => PromptForText(isEditing ? "Modifier l'action de journal" : "Action de journal",
                "Texte à ajouter au journal :", existing?.Value ?? string.Empty),
            MacroActionKind.Delay => PromptForText(isEditing ? "Modifier l'action d'attente" : "Action d'attente",
                "Durée en millisecondes (0 à 600000) :", existing?.Value ?? "1000"),
            MacroActionKind.ShowMessage => PromptForText(isEditing ? "Modifier l'action de message" : "Action de message",
                "Message à afficher :", existing?.Value ?? string.Empty),
            MacroActionKind.OpenUrl => PromptForText(isEditing ? "Modifier l'action d'ouverture d'URL" : "Action d'ouverture d'URL",
                "URL HTTP ou HTTPS :", existing?.Value ?? "https://"),
            MacroActionKind.PowerShell => PromptForScript(existing?.Value),
            MacroActionKind.Beep => existing?.Value ?? string.Empty,
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

    private MacroAction? PromptForSystemAction(MacroActionKind kind, MacroAction? existing)
    {
        var requiresAction = kind is MacroActionKind.EnableAction or MacroActionKind.DisableAction;
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
        panel.Children.Add(new TextBlock { Text = "Macro cible :", Margin = new Thickness(0, 0, 0, 5) });
        var macroChoices = Macros.Select((macro, index) => new SystemMacroChoice(macro, $"{index + 1}. {macro.Name}")).ToArray();
        var macroSelector = new ComboBox
        {
            ItemsSource = macroChoices,
            DisplayMemberPath = nameof(SystemMacroChoice.Label),
            Margin = new Thickness(0, 0, 0, 12),
            SelectedItem = existing is null ? macroChoices.FirstOrDefault() :
                macroChoices.FirstOrDefault(choice => Guid.TryParse(existing.Target, out var id) && choice.Macro.Id == id)
        };
        panel.Children.Add(macroSelector);

        ComboBox? actionSelector = null;
        if (requiresAction)
        {
            panel.Children.Add(new TextBlock { Text = "Action cible :", Margin = new Thickness(0, 0, 0, 5) });
            actionSelector = new ComboBox { DisplayMemberPath = nameof(SystemActionChoice.Label), Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(actionSelector);
            void UpdateActionChoices()
            {
                var targetMacro = (macroSelector.SelectedItem as SystemMacroChoice)?.Macro;
                var actionChoices = targetMacro?.Actions.Select((action, index) =>
                    new SystemActionChoice(action, $"{index + 1}. {ActionTargetName(action)}")).ToArray() ?? [];
                actionSelector.ItemsSource = actionChoices;
                actionSelector.SelectedItem = existing is null ? actionChoices.FirstOrDefault() :
                    actionChoices.FirstOrDefault(choice => Guid.TryParse(existing.Value, out var id) && choice.Action.Id == id);
            }
            macroSelector.SelectionChanged += (_, _) => UpdateActionChoices();
            UpdateActionChoices();
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button { Content = "Enregistrer", IsDefault = true };
        save.Click += (_, _) =>
        {
            if (macroSelector.SelectedItem is not SystemMacroChoice ||
                (requiresAction && actionSelector?.SelectedItem is not SystemActionChoice))
            {
                MessageBox.Show(dialog, "Sélectionnez une macro et, si nécessaire, une action cible.",
                    "Cible requise", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        if (dialog.ShowDialog() != true || macroSelector.SelectedItem is not SystemMacroChoice selected)
            return null;

        return new MacroAction
        {
            Kind = kind,
            Target = selected.Macro.Id.ToString(),
            Value = (actionSelector?.SelectedItem as SystemActionChoice)?.Action.Id.ToString() ?? string.Empty
        };
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
            MacroActionKind.Jump or MacroActionKind.If;

    private static bool IsTargetedSystemAction(MacroActionKind kind) =>
        kind is MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
            MacroActionKind.EnableAction or MacroActionKind.DisableAction;

    private static string SystemActionName(MacroActionKind kind) => kind switch
    {
        MacroActionKind.EnableMacro => "Système — activer une macro",
        MacroActionKind.DisableMacro => "Système — désactiver une macro",
        MacroActionKind.EnableAction => "Système — activer une action",
        MacroActionKind.DisableAction => "Système — désactiver une action",
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

    private static MacroAction FindSystemTargetAction(MacroDefinition macro, MacroAction action)
    {
        if (!Guid.TryParse(action.Value, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Identifiant d'action cible invalide.");
        var matches = macro.Actions.Where(item => item.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException($"L'action cible de « {macro.Name} » est introuvable ou son identifiant est dupliqué.");
    }

    private static MacroAction FindJumpTargetAction(MacroDefinition macro, MacroAction action)
    {
        if (!Guid.TryParse(action.Value, out var id) || id == Guid.Empty)
            throw new InvalidOperationException("Identifiant d'action Jump invalide.");
        var matches = macro.Actions.Where(item => item.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException($"L'action cible de Jump dans « {macro.Name} » est introuvable ou son identifiant est dupliqué.");
    }

    private static SystemActionResult ExecuteSystemAction(IEnumerable<MacroDefinition> macros, MacroAction action)
    {
        var macro = FindSystemTargetMacro(macros, action);
        switch (action.Kind)
        {
            case MacroActionKind.EnableMacro:
            case MacroActionKind.DisableMacro:
                var previousMacroState = macro.IsEnabled;
                macro.IsEnabled = action.Kind == MacroActionKind.EnableMacro;
                return new SystemActionResult(
                    $"Macro « {macro.Name} » {(macro.IsEnabled ? "activée" : "désactivée")}.",
                    macro.Id, null, previousMacroState, macro.IsEnabled);
            case MacroActionKind.EnableAction:
            case MacroActionKind.DisableAction:
                var target = FindSystemTargetAction(macro, action);
                var previousActionState = target.IsEnabled;
                target.IsEnabled = action.Kind == MacroActionKind.EnableAction;
                return new SystemActionResult(
                    $"Action « {ActionTargetName(target)} » de « {macro.Name} » {(target.IsEnabled ? "activée" : "désactivée")}.",
                    macro.Id, target.Id, previousActionState, target.IsEnabled);
            default:
                throw new InvalidOperationException($"Action système inconnue : {action.Kind}");
        }
    }

    private static string AcceptRuntimeStateChange(string cleanConfiguration, SystemActionResult result)
    {
        if (result.PreviousState == result.CurrentState)
            return cleanConfiguration;

        var configuration = JsonSerializer.Deserialize<ConfigurationDocument>(cleanConfiguration, JsonOptions)
            ?? throw new JsonException("La référence de configuration est invalide.");
        var macro = configuration.Macros.SingleOrDefault(item => item.Id == result.MacroId);
        if (macro is null)
            return cleanConfiguration;

        // Preserve a manually changed state in the dirty comparison.
        if (result.ActionId is Guid actionId)
        {
            var action = macro.Actions.SingleOrDefault(item => item.Id == actionId);
            if (action is null || action.IsEnabled != result.PreviousState)
                return cleanConfiguration;
            action.IsEnabled = result.CurrentState;
        }
        else
        {
            if (macro.IsEnabled != result.PreviousState)
                return cleanConfiguration;
            macro.IsEnabled = result.CurrentState;
        }
        return JsonSerializer.Serialize(configuration, JsonOptions);
    }

    private static void ValidateEntityIds(IEnumerable<MacroDefinition> macros)
    {
        var macroIds = new HashSet<Guid>();
        var actionIds = new HashSet<Guid>();
        foreach (var macro in macros)
        {
            if (macro.Id == Guid.Empty || !macroIds.Add(macro.Id))
                throw new JsonException("Identifiant de macro vide ou dupliqué dans la configuration.");
            foreach (var action in macro.Actions)
                if (action.Id == Guid.Empty || !actionIds.Add(action.Id))
                    throw new JsonException("Identifiant d'action vide ou dupliqué dans la configuration.");
        }
    }

    private static void RefreshSystemActionDescriptions(IEnumerable<MacroDefinition> macros)
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

                var macro = FindSystemTargetMacro(macros, action);
                var target = string.Empty;
                if (action.Kind is MacroActionKind.EnableAction or MacroActionKind.DisableAction)
                {
                    var targetAction = FindSystemTargetAction(macro, action);
                    target = $" — {macro.Actions.IndexOf(targetAction) + 1}. {ActionTargetName(targetAction)}";
                }
                action.SystemDisplayName = $"{SystemActionName(action.Kind)} : {macro.Name}{target}";
            }
            catch (InvalidOperationException)
            {
                action.SystemDisplayName = $"{SystemActionName(action.Kind)} : cible introuvable";
            }
        }
    }

    private void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMacro is not MacroDefinition macro ||
            ConfigurationTree.SelectedItem is not MacroAction action)
            return;

        if (MessageBox.Show(this, $"Supprimer l'action « {action.DisplayName} » de la macro « {macro.Name} » ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        macro.Actions.Remove(action);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros);
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
            WriteLog($"Erreur de configuration du plugin {pluginId} : {exception.Message}");
            MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private static PluginActionData ToPluginData(MacroAction action) =>
        new(action.ActionId, action.Value, action.Target);

    private static PluginActionData ToPluginData(PluginStartupConfiguration configuration) =>
        new(configuration.ConfigurationId, configuration.Value, configuration.Target);

    private static PluginStartupData ToPluginStartupData(PluginStartupConfiguration configuration) =>
        new(configuration.ConfigurationId, configuration.InstanceId, configuration.Value, configuration.Target);

    private PluginStartupConfiguration FindInstanceConfiguration(
        IPlugin plugin, IPluginInstanceProvider provider, string instanceId)
    {
        var matches = PluginStartups
            .Where(configuration => string.Equals(configuration.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase) &&
                provider.IsInstanceConfiguration(configuration.ConfigurationId) &&
                string.Equals(configuration.InstanceId, instanceId, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException($"Configuration du plugin {plugin.Name} introuvable ou dupliquée.");
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

    private void RefreshPluginDescriptions(IEnumerable<MacroDefinition> macros)
    {
        foreach (var action in macros.SelectMany(macro => macro.Actions).Where(action => action.Kind == MacroActionKind.Plugin))
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
                WriteLog($"Impossible d'afficher l'action {action.PluginId}/{action.ActionId} : {exception.Message}");
            }
        }
    }

    private void EnsurePluginStartupConfiguration(string pluginId)
    {
        if (PluginStartups.Any(configuration =>
                string.Equals(configuration.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
            return;

        var plugin = _pluginLoader.Find(pluginId);
        if (plugin is null)
            return;
        var configuration = plugin.CreateDefaultStartupConfiguration();
        PluginStartups.Add(CreatePluginStartupConfiguration(plugin, configuration));
        RefreshPluginStartupDescriptions();
    }

    private void RefreshPluginStartupDescriptions()
    {
        foreach (var configuration in PluginStartups)
        {
            var plugin = _pluginLoader.Find(configuration.PluginId);
            if (plugin is null)
            {
                configuration.DisplayName = $"{configuration.PluginId} — plugin introuvable";
                configuration.CanConfigure = false;
                continue;
            }
            configuration.DisplayName = plugin.DescribeStartup(ToPluginStartupData(configuration));
            configuration.CanConfigure = plugin.HasStartupConfiguration;
        }
    }

    private static PluginStartupConfiguration CreatePluginStartupConfiguration(
        IPlugin plugin, PluginStartupData configuration) =>
        new()
        {
            PluginId = plugin.Id,
            ConfigurationId = configuration.ConfigurationId,
            InstanceId = string.IsNullOrWhiteSpace(configuration.InstanceId)
                ? Guid.NewGuid().ToString()
                : configuration.InstanceId,
            Value = configuration.Value,
            Target = configuration.Target
        };

    private async void RunMacro_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMacro is MacroDefinition macro)
            await ExecuteMacroInSessionAsync(macro, _sessionCancellation.Token);
    }

    private async void SimulateEvent_Click(object sender, RoutedEventArgs e)
    {
        var eventName = PromptForText("Déclencher un événement", "Nom exact de l'événement :", "Demo.Start");
        if (string.IsNullOrWhiteSpace(eventName))
            return;

        await DispatchEventAsync(eventName.Trim(), null);
    }

    private async Task DispatchEventAsync(
        string eventName,
        string? payload,
        CancellationToken? sessionToken = null)
    {
        var cancellationToken = sessionToken ?? _sessionCancellation.Token;
        if (cancellationToken.IsCancellationRequested)
            return;

        var matchingMacros = Macros
            .Where(macro => macro.IsEnabled && macro.MatchesEvent(eventName))
            .ToList();
        var detail = string.IsNullOrWhiteSpace(payload) ? string.Empty : $" — {payload}";
        WriteLog($"Événement reçu : {eventName}{detail} — {matchingMacros.Count} macro(s) correspondante(s).", eventName);
        foreach (var macro in matchingMacros)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            if (!macro.IsEnabled)
                continue;
            await ExecuteMacroInSessionAsync(macro, cancellationToken);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Macros.Count > 0)
            SelectTreeItem(Macros[0]);
        try
        {
            await StartConfiguredPluginsAsync();
        }
        catch (AggregateException exception)
        {
            MessageBox.Show(this, exception.Message, "Erreur de démarrage de plugin",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await DispatchEventAsync(ConfigurationLoadedEventName, null);
        await DispatchEventAsync(ApplicationStartupEventName, null);
    }

    private async void StopPlugins_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ResetPluginsAsync();
        }
        catch (AggregateException exception)
        {
            MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (SerializeConfiguration() == _cleanConfiguration)
            return;

        var result = MessageBox.Show(this,
            "La configuration a été modifiée. Voulez-vous l'enregistrer avant de quitter ?\n\n" +
            "Oui : enregistrer et quitter. Non : quitter sans enregistrer. Annuler : rester dans l'application.",
            "Modifications non enregistrées", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (result == MessageBoxResult.Cancel || (result == MessageBoxResult.Yes && !SaveConfiguration()))
            e.Cancel = true;
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _sessionCancellation.Cancel();
        try
        {
            await Task.WhenAll(_activeMacroExecutions.ToArray());
        }
        catch (Exception exception)
        {
            WriteLog($"Erreur d'arrêt des macros : {exception.Message}");
        }
        foreach (var plugin in _pluginLoader.Plugins)
        {
            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception exception)
            {
                WriteLog($"Erreur de fermeture du plugin {plugin.Id} : {exception.Message}");
            }
        }
        _sessionCancellation.Dispose();
    }

    private async Task ResetPluginsAsync()
    {
        var failures = new List<Exception>();
        foreach (var plugin in _pluginLoader.Plugins)
        {
            try
            {
                await plugin.ResetAsync();
            }
            catch (Exception exception)
            {
                WriteLog($"Erreur d'arrêt du plugin {plugin.Id} : {exception.Message}");
                failures.Add(exception);
            }
        }
        if (failures.Count > 0)
            throw new AggregateException("Certains plugins n'ont pas pu être arrêtés.", failures);
        WriteLog("Plugins arrêtés.");
    }

    private async Task StartConfiguredPluginsAsync()
    {
        var failures = new List<Exception>();
        foreach (var configuration in PluginStartups.ToArray())
        {
            var plugin = _pluginLoader.Find(configuration.PluginId);
            if (plugin is null)
            {
                var error = new InvalidOperationException($"Plugin introuvable : {configuration.PluginId}");
                WriteLog($"Démarrage impossible : {error.Message}");
                failures.Add(error);
                continue;
            }

            try
            {
                await plugin.StartupAsync(ToPluginStartupData(configuration),
                    new PluginHost(this, new ActionContext(this, _sessionCancellation.Token, null)),
                    _sessionCancellation.Token);
            }
            catch (OperationCanceledException) when (_sessionCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                WriteLog($"Erreur de démarrage du plugin {plugin.Id} : {exception.Message}");
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("Certains plugins n'ont pas pu démarrer.", failures);
        WriteLog("Démarrage des plugins terminé.");
    }

    private async Task RestartConfiguredPluginsAsync()
    {
        try
        {
            await StartNewSessionAsync();
            await StartConfiguredPluginsAsync();
        }
        catch (AggregateException exception)
        {
            WriteLog($"Erreur de redémarrage des plugins : {exception.Message}");
            MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfigurationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedMacro = e.NewValue switch
        {
            MacroDefinition macro => macro,
            MacroAction action => Macros.FirstOrDefault(macro => macro.Actions.Contains(action)),
            MacroEvent macroEvent => macroEvent.Macro,
            _ => null
        };
        EditMacroButton.IsEnabled = _selectedMacro is not null;
        EditActionButton.IsEnabled = e.NewValue is MacroAction selectedAction &&
                                     selectedAction.Kind != MacroActionKind.Beep;
    }

    private void SelectTreeItem(MacroDefinition macro, object? item = null)
    {
        ConfigurationTree.UpdateLayout();
        if (ConfigurationTree.ItemContainerGenerator.ContainerFromItem(macro) is not TreeViewItem parent)
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

    private async void AddPluginStartup_Click(object sender, RoutedEventArgs e)
    {
        var plugin = PromptForPluginStartupTarget();
        if (plugin is null)
            return;

        var configuration = ConfigurePluginStartup(plugin, existing: null);
        if (configuration is null)
            return;
        PluginStartups.Add(configuration);
        RefreshPluginStartupDescriptions();
        RefreshPluginDescriptions(Macros);
        SelectPluginStartupItem(configuration);
        WriteLog($"Démarrage ajouté : {configuration.DisplayName}.");
        await RestartConfiguredPluginsAsync();
    }

    private async void EditPluginStartupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PluginStartupConfiguration configuration })
            return;
        await EditPluginStartupAsync(configuration);
    }

    private async Task EditPluginStartupAsync(PluginStartupConfiguration configuration)
    {
        var plugin = _pluginLoader.Find(configuration.PluginId);
        if (plugin is null)
        {
            MessageBox.Show(this, $"Plugin introuvable : {configuration.PluginId}", "Plugin indisponible",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var updated = ConfigurePluginStartup(plugin, configuration);
        if (updated is null)
            return;
        configuration.ConfigurationId = updated.ConfigurationId;
        configuration.Value = updated.Value;
        configuration.Target = updated.Target;
        RefreshPluginStartupDescriptions();
        RefreshPluginDescriptions(Macros);
        WriteLog($"Démarrage modifié : {configuration.DisplayName}.");
        await RestartConfiguredPluginsAsync();
    }

    private async void RemovePluginStartupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PluginStartupConfiguration configuration })
            return;

        var pluginActions = Macros.SelectMany(macro => macro.Actions)
            .Where(action => action.Kind == MacroActionKind.Plugin &&
                string.Equals(action.PluginId, configuration.PluginId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pluginActions.Any(action => string.Equals(action.InstanceId, configuration.InstanceId, StringComparison.Ordinal)))
        {
            MessageBox.Show(this, "Ce démarrage est utilisé par une ou plusieurs actions. Supprimez ou reconfigurez d'abord ces actions.",
                "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (PluginStartups.Count(item =>
                string.Equals(item.PluginId, configuration.PluginId, StringComparison.OrdinalIgnoreCase)) == 1 &&
            pluginActions.Length > 0)
        {
            MessageBox.Show(this, "Ce plugin est encore utilisé par une macro et doit conserver un démarrage.",
                "Suppression impossible", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Supprimer « {configuration.DisplayName} » ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        PluginStartups.Remove(configuration);
        RefreshPluginDescriptions(Macros);
        WriteLog($"Démarrage supprimé : {configuration.DisplayName}.");
        await RestartConfiguredPluginsAsync();
    }

    private PluginStartupConfiguration? ConfigurePluginStartup(
        IPlugin plugin, PluginStartupConfiguration? existing)
    {
        try
        {
            var host = new PluginHost(this, new ActionContext(this, _sessionCancellation.Token, null));
            var data = plugin.ConfigureStartup(existing is null ? null : ToPluginStartupData(existing), host);
            if (data is null)
                return null;

            var instanceId = existing?.InstanceId ?? data.InstanceId;
            if (string.IsNullOrWhiteSpace(instanceId) ||
                PluginStartups.Any(item => !ReferenceEquals(item, existing) &&
                    string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal)))
                instanceId = Guid.NewGuid().ToString();
            return new PluginStartupConfiguration
            {
                PluginId = plugin.Id,
                ConfigurationId = data.ConfigurationId,
                InstanceId = instanceId,
                Value = data.Value,
                Target = data.Target
            };
        }
        catch (Exception exception)
        {
            WriteLog($"Erreur de configuration du démarrage {plugin.Id} : {exception.Message}");
            MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private IPlugin? PromptForPluginStartupTarget()
    {
        var plugins = _pluginLoader.Plugins.ToArray();
        if (plugins.Length == 0)
        {
            MessageBox.Show(this, "Aucun plugin n'est chargé.", "Plugin indisponible",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var dialog = new Window
        {
            Title = "Ajouter un démarrage de plugin",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.White,
            Padding = new Thickness(18)
        };
        var panel = new StackPanel { Width = 320 };
        panel.Children.Add(new TextBlock { Text = "Plugin à démarrer :", Margin = new Thickness(0, 0, 0, 8) });
        var selector = new ComboBox
        {
            ItemsSource = plugins,
            DisplayMemberPath = nameof(IPlugin.Name),
            SelectedIndex = 0,
            Padding = new Thickness(6)
        };
        panel.Children.Add(selector);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Annuler", IsCancel = true };
        var add = new Button { Content = "Continuer", IsDefault = true };
        add.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? selector.SelectedItem as IPlugin : null;
    }

    private void SelectPluginStartupItem(PluginStartupConfiguration configuration)
    {
        ConfigurationTree.UpdateLayout();
        if (ConfigurationTree.ItemContainerGenerator.ContainerFromItem(_startupSection) is not TreeViewItem parent)
            return;
        parent.IsExpanded = true;
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(configuration) is TreeViewItem child)
        {
            child.IsSelected = true;
            child.BringIntoView();
        }
    }

    private void EditMacroItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroDefinition macro })
        {
            SelectTreeItem(macro);
            EditMacro_Click(sender, e);
        }
    }

    private void RemoveMacroItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroDefinition macro })
        {
            SelectTreeItem(macro);
            RemoveMacro_Click(sender, e);
        }
    }

    private void EditEventItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroEvent macroEvent })
        {
            SelectTreeItem(macroEvent.Macro, macroEvent);
            EditEvent(macroEvent);
        }
    }

    private void RemoveEventItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroEvent macroEvent })
        {
            SelectTreeItem(macroEvent.Macro, macroEvent);
            RemoveEvent(macroEvent);
        }
    }

    private void EditEvent(MacroEvent macroEvent)
    {
        var macro = macroEvent.Macro;
        var name = PromptForText("Modifier l'événement", "Nom exact de l'événement :", macroEvent.Name);
        if (name is null)
            return;

        name = name.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Saisissez un nom d'événement.", "Événement requis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var index = macro.EventItems.IndexOf(macroEvent);
        if (index < 0)
            return;
        if (macro.EventNames.Where((_, i) => i != index)
            .Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Cet événement est déjà associé à la macro.", "Événement déjà présent",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var previousName = macroEvent.Name;
        macro.EventNames[index] = name;
        WriteLog($"Événement « {previousName} » modifié en « {name} » dans « {macro.Name} ».");
    }

    private void RemoveEvent(MacroEvent macroEvent)
    {
        var macro = macroEvent.Macro;
        if (MessageBox.Show(this, $"Supprimer l'événement « {macroEvent.Name} » de la macro « {macro.Name} » ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var index = macro.EventItems.IndexOf(macroEvent);
        if (index < 0)
            return;
        macro.EventNames.RemoveAt(index);
        SelectTreeItem(macro);
        WriteLog($"Événement « {macroEvent.Name} » supprimé de « {macro.Name} ».");
    }

    private void EditActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroAction action })
        {
            var macro = Macros.FirstOrDefault(item => item.Actions.Contains(action));
            if (macro is not null)
            {
                SelectTreeItem(macro, action);
                EditAction_Click(sender, e);
            }
        }
    }

    private void RemoveActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroAction action })
        {
            var macro = Macros.FirstOrDefault(item => item.Actions.Contains(action));
            if (macro is not null)
            {
                SelectTreeItem(macro, action);
                RemoveAction_Click(sender, e);
            }
        }
    }

    private void TreeItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsItemControl(e.OriginalSource) || sender is not TreeViewItem item)
            return;
        e.Handled = true;
        item.IsSelected = true;
        if (item.DataContext is PluginStartupConfiguration startupConfiguration)
        {
            if (startupConfiguration.CanConfigure)
                _ = EditPluginStartupAsync(startupConfiguration);
        }
        else if (item.DataContext is MacroEvent macroEvent)
            EditEvent(macroEvent);
        else if (item.DataContext is MacroAction action)
        {
            if (action.Kind == MacroActionKind.Beep)
                MessageBox.Show(this, "Cette action n'a pas de paramètres à modifier.", "Modification impossible",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                EditAction_Click(sender, e);
        }
        else if (_selectedMacro is not null)
            EditMacro_Click(sender, e);
    }

    private static bool IsItemControl(object source)
    {
        var current = source as DependencyObject;
        while (current is not null && current is not TreeViewItem)
        {
            if (current is ButtonBase)
                return true;
            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                ContentElement content => ContentOperations.GetParent(content) ??
                    (content as FrameworkContentElement)?.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }
        return false;
    }

    private async Task ExecuteMacroInSessionAsync(MacroDefinition macro, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var execution = ExecuteMacroAsync(macro, cancellationToken);
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
        MacroDefinition macro, CancellationToken cancellationToken, int callDepth = 0)
    {
        if (cancellationToken.IsCancellationRequested)
            return;
        if (callDepth >= MaximumMacroCallDepth)
            throw new InvalidOperationException($"La profondeur maximale d'appel de macro ({MaximumMacroCallDepth}) est dépassée.");

        WriteLog($"Exécution de la macro « {macro.Name} ».");
        var actionIds = macro.Actions.Select(action => action.Id).ToArray();
        var actionIndex = 0;
        var actionExecutions = 0;
        object? previousActionResult = null;
        while (actionIndex < actionIds.Length)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            if (++actionExecutions > MaximumActionExecutionsPerMacro)
            {
                WriteLog($"Limite de {MaximumActionExecutionsPerMacro} exécutions d'actions atteinte dans « {macro.Name} ».");
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
                    : action.Value.Length == 0 ? null : action.Value);
            try
            {
                switch (action.Kind)
                {
                    case MacroActionKind.Log:
                        actionContext.Log(action.Value);
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
                        await RunPowerShellAsync(action.Value, actionContext, cancellationToken);
                        break;
                    case MacroActionKind.Plugin:
                        var plugin = _pluginLoader.Find(action.PluginId)
                            ?? throw new InvalidOperationException($"Plugin introuvable : {action.PluginId}");
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
                                await ExecuteMacroAsync(targetMacro, cancellationToken, callDepth + 1);
                                if (stopAfterJump)
                                    nextActionIndex = actionIds.Length;
                                break;
                            case JumpMode.GotoAction:
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
                actionContext.Log($"Erreur dans « {macro.Name} » ({action.Kind}) : {exception.Message}");
                actionContext.Result = null;
            }

            if (stopAfterJump)
                break;
            previousActionResult = actionContext.Result;
            actionIndex = nextActionIndex;
        }
        if (!cancellationToken.IsCancellationRequested)
            WriteLog($"Macro terminée : « {macro.Name} ».");
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

    private void SaveConfiguration_Click(object sender, RoutedEventArgs e) => SaveConfiguration();

    private async void ReloadConfiguration_Click(object sender, RoutedEventArgs e)
    {
        ConfigurationDocument newConfiguration;
        try
        {
            newConfiguration = ReadConfiguration();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            WriteLog($"Impossible de recharger la configuration : {exception.Message}");
            MessageBox.Show(this, $"La configuration actuelle est conservée.\n{exception.Message}",
                "Erreur de rechargement", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            await StartNewSessionAsync();
        }
        catch (AggregateException exception)
        {
            WriteLog($"Impossible de recharger la configuration : {exception.Message}");
            MessageBox.Show(this, exception.Message, "Erreur de rechargement",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Macros.Clear();
        PluginStartups.Clear();
        foreach (var configuration in newConfiguration.PluginStartups)
            PluginStartups.Add(configuration);
        foreach (var macro in newConfiguration.Macros)
            Macros.Add(macro);
        RefreshPluginStartupDescriptions();
        RefreshPluginDescriptions(Macros);
        _cleanConfiguration = SerializeConfiguration();
        if (Macros.Count > 0)
            SelectTreeItem(Macros[0]);

        WriteLog($"Configuration rechargée : {Macros.Count} macro(s).");
        try
        {
            await StartConfiguredPluginsAsync();
        }
        catch (AggregateException exception)
        {
            MessageBox.Show(this, exception.Message, "Erreur de démarrage de plugin",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        await DispatchEventAsync(ConfigurationLoadedEventName, null);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "EventGhost .NET\nApplication d'automatisation événementielle pour Windows.\n" +
            "Cette première version gère des macros, des déclencheurs manuels et des actions en séquence.",
            "À propos", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadConfiguration()
    {
        try
        {
            var configuration = ReadConfiguration();
            foreach (var startup in configuration.PluginStartups)
                PluginStartups.Add(startup);
            foreach (var macro in configuration.Macros)
                Macros.Add(macro);
            RefreshPluginStartupDescriptions();
            RefreshPluginDescriptions(Macros);
            WriteLog(File.Exists(_configurationPath)
                ? $"Configuration chargée : {Macros.Count} macro(s)."
                : "Configuration initiale créée en mémoire. Ajoutez vos macros puis enregistrez.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            WriteLog($"Impossible de charger la configuration : {exception.Message}");
            Macros.Add(CreateSampleMacro());
        }
    }

    private ConfigurationDocument ReadConfiguration()
    {
        if (!File.Exists(_configurationPath))
            return new ConfigurationDocument { Macros = [CreateSampleMacro()] };

        var json = File.ReadAllText(_configurationPath);
        using var document = JsonDocument.Parse(json);
        var configuration = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => new ConfigurationDocument
            {
                Macros = JsonSerializer.Deserialize<ObservableCollection<MacroDefinition>>(json, JsonOptions)
                    ?? throw new JsonException("Le fichier ne contient pas de configuration de macros.")
            },
            JsonValueKind.Object => JsonSerializer.Deserialize<ConfigurationDocument>(json, JsonOptions)
                ?? throw new JsonException("Le fichier ne contient pas de configuration."),
            _ => throw new JsonException("Le fichier de configuration doit contenir un tableau ou un objet.")
        };
        configuration.PluginStartups ??= [];
        configuration.Macros ??= [];

        MigrateLegacyStartupActions(configuration);
        foreach (var macro in configuration.Macros)
        {
            if (macro.EventNames.Any(string.IsNullOrWhiteSpace))
                throw new JsonException($"La macro « {macro.Name} » contient un événement vide.");
        }
        foreach (var action in configuration.Macros.SelectMany(macro => macro.Actions))
        {
            if (action.Kind is not (MacroActionKind.Log or MacroActionKind.Delay or MacroActionKind.Beep or
                MacroActionKind.ShowMessage or MacroActionKind.OpenUrl or MacroActionKind.PowerShell or MacroActionKind.Plugin or
                MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or MacroActionKind.EnableAction or MacroActionKind.DisableAction or
                MacroActionKind.Jump or MacroActionKind.If))
            {
                var match = _pluginLoader.FindLegacy(action.Kind.ToString());
                if (match is not null)
                {
                    action.Kind = MacroActionKind.Plugin;
                    action.PluginId = match.Value.Plugin.Id;
                    action.ActionId = match.Value.Action.Id;
                }
            }
        }

        EnsurePluginStartupConfigurations(configuration.Macros, configuration.PluginStartups);
        MigratePluginInstances(configuration.Macros, configuration.PluginStartups, _pluginLoader.Plugins);
        ValidateEntityIds(configuration.Macros);
        ValidateStartupConfigurationIds(configuration.PluginStartups);
        RefreshSystemActionDescriptions(configuration.Macros);
        return configuration;
    }

    private static void MigrateLegacyStartupActions(ConfigurationDocument configuration)
    {
        var instanceIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var macro in configuration.Macros)
        {
            foreach (var action in macro.Actions.ToArray())
            {
                var isLogitechStartup = action.Kind == MacroActionKind.LogitechStart ||
                    (action.Kind == MacroActionKind.Plugin &&
                     string.Equals(action.PluginId, "Logitech", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(action.ActionId, "LogitechStart", StringComparison.Ordinal));
                if (!isLogitechStartup)
                    continue;

                var startup = configuration.PluginStartups.FirstOrDefault(item =>
                    string.Equals(item.PluginId, "Logitech", StringComparison.OrdinalIgnoreCase) &&
                    item.ConfigurationId == "LogitechStart" &&
                    string.Equals(item.Target, action.Target, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Value, action.Value, StringComparison.Ordinal));
                if (startup is null)
                {
                    startup = new PluginStartupConfiguration
                    {
                        PluginId = "Logitech",
                        ConfigurationId = "LogitechStart",
                        InstanceId = string.IsNullOrWhiteSpace(action.InstanceId)
                            ? Guid.NewGuid().ToString()
                            : action.InstanceId,
                        Value = action.Value,
                        Target = action.Target
                    };
                    configuration.PluginStartups.Add(startup);
                }
                if (!string.IsNullOrWhiteSpace(action.InstanceId))
                    instanceIdMap[action.InstanceId] = startup.InstanceId;
                action.InstanceId = startup.InstanceId;
                macro.Actions.Remove(action);
            }
        }

        foreach (var action in configuration.Macros.SelectMany(macro => macro.Actions)
                     .Where(action => string.Equals(action.PluginId, "Logitech", StringComparison.OrdinalIgnoreCase) &&
                                      !string.IsNullOrWhiteSpace(action.InstanceId)))
            if (instanceIdMap.TryGetValue(action.InstanceId!, out var migratedId))
                action.InstanceId = migratedId;
    }

    private void EnsurePluginStartupConfigurations(
        IEnumerable<MacroDefinition> macros, ObservableCollection<PluginStartupConfiguration> configurations)
    {
        var usedPluginIds = macros.SelectMany(macro => macro.Actions)
            .Where(action => action.Kind == MacroActionKind.Plugin)
            .Select(action => action.PluginId)
            .Where(pluginId => !string.IsNullOrWhiteSpace(pluginId))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var pluginId in usedPluginIds)
        {
            if (configurations.Any(configuration =>
                    string.Equals(configuration.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (_pluginLoader.Find(pluginId) is not IPlugin plugin)
                continue;
            var data = plugin.CreateDefaultStartupConfiguration();
            configurations.Add(CreatePluginStartupConfiguration(plugin, data));
        }
    }

    private static void ValidateStartupConfigurationIds(IEnumerable<PluginStartupConfiguration> configurations)
    {
        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configuration in configurations)
        {
            if (string.IsNullOrWhiteSpace(configuration.PluginId) ||
                string.IsNullOrWhiteSpace(configuration.ConfigurationId) ||
                string.IsNullOrWhiteSpace(configuration.InstanceId) ||
                configuration.Value is null || configuration.Target is null ||
                !instanceIds.Add(configuration.InstanceId))
                throw new JsonException("Configuration de démarrage vide ou identifiant d'instance dupliqué.");
        }
    }

    private static void MigratePluginInstances(
        IEnumerable<MacroDefinition> macros,
        IEnumerable<PluginStartupConfiguration> startupConfigurations,
        IEnumerable<IPlugin> plugins)
    {
        foreach (var instancePlugin in plugins)
        {
            if (instancePlugin is not IPluginInstanceProvider plugin)
                continue;
            var actions = macros.SelectMany(macro => macro.Actions)
                .Where(action => action.Kind == MacroActionKind.Plugin &&
                    string.Equals(action.PluginId, instancePlugin.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var configurations = startupConfigurations
                .Where(configuration => string.Equals(configuration.PluginId, instancePlugin.Id, StringComparison.OrdinalIgnoreCase) &&
                    plugin.IsInstanceConfiguration(configuration.ConfigurationId))
                .ToArray();
            foreach (var configuration in configurations)
                if (string.IsNullOrEmpty(configuration.InstanceId))
                    configuration.InstanceId = Guid.NewGuid().ToString();
            foreach (var action in actions.Where(action => plugin.RequiresInstance(action.ActionId)))
            {
                if (!string.IsNullOrEmpty(action.InstanceId) &&
                    configurations.Any(configuration => configuration.InstanceId == action.InstanceId))
                    continue;
                var matching = configurations
                    .Where(configuration => plugin.MatchesInstance(ToPluginData(action), ToPluginData(configuration)))
                    .ToArray();
                if (matching.Length == 1)
                    action.InstanceId = matching[0].InstanceId;
            }
        }
    }

    private async Task StartNewSessionAsync()
    {
        var previousSession = _sessionCancellation;
        previousSession.Cancel();

        try
        {
            var activeExecutions = _activeMacroExecutions.ToArray();
            if (activeExecutions.Length > 0)
                await Task.WhenAll(activeExecutions);
        }
        finally
        {
            try
            {
                await ResetPluginsAsync();
            }
            finally
            {
                _sessionCancellation = new CancellationTokenSource();
                previousSession.Dispose();
            }
        }
    }

    private bool SaveConfiguration()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configurationPath)!);
            var json = SerializeConfiguration();
            File.WriteAllText(_configurationPath, json);
            _cleanConfiguration = json;
            WriteLog($"Configuration enregistrée : {Macros.Count} macro(s).");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteLog($"Échec de l'enregistrement : {exception.Message}");
            MessageBox.Show(this, $"La configuration n'a pas pu être enregistrée.\n{exception.Message}",
                "Erreur d'enregistrement", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private string SerializeConfiguration() =>
        JsonSerializer.Serialize(new ConfigurationDocument
        {
            PluginStartups = PluginStartups,
            Macros = Macros
        }, JsonOptions);

    private static MacroDefinition CreateSampleMacro() =>
        new()
        {
            Name = "Bienvenue",
            EventNames = ["Demo.Start"],
            Actions = [new MacroAction { Kind = MacroActionKind.Log, Value = "EventGhost .NET est prêt." }]
        };

    private void CopyEventName_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedEventName() is not string eventName)
            return;

        try
        {
            Clipboard.SetText(eventName);
        }
        catch (ExternalException exception)
        {
            WriteLog($"Impossible de copier le nom de l'événement : {exception.Message}");
            MessageBox.Show(this, exception.Message, "Erreur de presse-papiers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AssignEventName_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedEventName() is not string eventName)
            return;
        if (_selectedMacro is not MacroDefinition macro)
        {
            MessageBox.Show(this, "Créez ou sélectionnez d'abord une macro.", "Aucune macro sélectionnée",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (macro.EventNames.Contains(eventName, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Cet événement est déjà associé à la macro.", "Événement déjà présent",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Ajouter « {eventName} » aux déclencheurs de « {macro.Name} » ?",
                "Ajouter un déclencheur", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        macro.EventNames.Add(eventName);
        WriteLog($"Événement « {eventName} » ajouté à la macro « {macro.Name} ».");
    }

    private string? GetSelectedEventName()
    {
        if (LogList.SelectedItem is EventLogEntry entry && !string.IsNullOrWhiteSpace(entry.EventName))
            return entry.EventName;

        MessageBox.Show(this, "Sélectionnez un événement reçu dans le journal.", "Aucun événement sélectionné",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void WriteLog(string message) => WriteLog(message, null);

    private void WriteLog(string message, string? eventName)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogList.Items.Add(eventName is null ? line : new EventLogEntry(line, eventName));
        while (LogList.Items.Count > 500)
            LogList.Items.RemoveAt(0);
        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private string? PromptForText(string title, string prompt, string initialValue)
    {
        var value = Interaction.InputBox(prompt, title, initialValue);
        return string.IsNullOrEmpty(value) ? null : value;
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
            Background = Brushes.White,
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
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White
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

    private string? PromptForScript(string? initialScript = null)
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
            Text = initialScript ?? "# Exemple : écrire dans le journal\nWrite-Output 'Bonjour depuis PowerShell'"
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
            Height = 470,
            MinWidth = 680,
            MinHeight = 400,
            Background = Brushes.White
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
            Content = initialScript is null ? "Ajouter le script" : "Enregistrer le script",
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White
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
        panel.Children.Add(editor);
        dialog.Content = panel;

        return dialog.ShowDialog() == true ? editor.Text : null;
    }

    private static async Task RunPowerShellAsync(
        string script, IActionContext context, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var markerPrefix = $"__EGDN_{Guid.NewGuid():N}_";
        var logMarker = $"{markerPrefix}LOG:";
        var eventMarker = $"{markerPrefix}EVENT:";
        var resultMarker = $"{markerPrefix}RESULT:";
        var initialResult = context.Result is string initialValue
            ? $"[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(initialValue))}'))"
            : "$null";
        var wrappedScript = $$"""
            $eg = [pscustomobject]@{ Result = {{initialResult}} }
            $eg | Add-Member -MemberType ScriptMethod -Name Log -Value {
                param([object]$Message)
                $payload = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$Message))
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
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Impossible de démarrer PowerShell.");
            var outputTask = ReadPowerShellOutputAsync(
                process.StandardOutput, context, logMarker, eventMarker, resultMarker);
            var errorTask = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                cancellationToken);
            try
            {
                await process.WaitForExitAsync(linkedCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await outputTask;
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
        StreamReader output, IActionContext context, string logMarker, string eventMarker, string resultMarker)
    {
        while (await output.ReadLineAsync() is { } line)
        {
            if (line.StartsWith(logMarker, StringComparison.Ordinal))
            {
                context.Log(DecodePowerShellPayload(line[logMarker.Length..]));
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
            Background = Brushes.White,
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
            new(MacroActionKind.OpenUrl, "Ouvrir une URL"),
            new(MacroActionKind.PowerShell, "Script PowerShell"),
            new(MacroActionKind.EnableMacro, "Système — activer une macro"),
            new(MacroActionKind.DisableMacro, "Système — désactiver une macro"),
            new(MacroActionKind.EnableAction, "Système — activer une action"),
            new(MacroActionKind.DisableAction, "Système — désactiver une action"),
            new(MacroActionKind.Jump, "Système — Jump"),
            new(MacroActionKind.If, "Système — If")
        };
        choices.AddRange(CreatePluginActionChoices(_pluginLoader.Plugins, PluginStartups));
        var selector = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = 0,
            Padding = new Thickness(6)
        };
        selector.DisplayMemberPath = nameof(ActionChoice.Name);
        panel.Children.Add(selector);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Annuler", IsCancel = true };
        var add = new Button { Content = "Ajouter", IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)), Foreground = Brushes.White };
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