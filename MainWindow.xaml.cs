using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private sealed record ActionChoice(
        MacroActionKind Kind, string Name, string? PluginId = null, string? ActionId = null, string? InstanceId = null);

    private sealed record EventLogEntry(string Text, string EventName)
    {
        public override string ToString() => Text;
    }

    private sealed record SystemMacroChoice(MacroDefinition Macro, string Label);
    private sealed record SystemActionChoice(MacroAction Action, string Label);
    private sealed record SystemActionResult(
        string Message, Guid MacroId, Guid? ActionId, bool PreviousState, bool CurrentState);

    private sealed class PluginHost(MainWindow window, CancellationToken sessionToken) : IPluginHost
    {
        public Window Owner => window;
        public IReadOnlyList<PluginActionData> GetConfiguredActions(string pluginId) =>
            window.Macros.SelectMany(macro => macro.Actions)
                .Where(action => string.Equals(action.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                .Select(action => new PluginActionData(action.ActionId, action.Value, action.Target)).ToArray();
        public void Log(string message) => window.Dispatcher.BeginInvoke(new Action(() => window.WriteLog(message)));
        public void RaiseEvent(string name, string? payload) => window.Dispatcher.BeginInvoke(
            new Action(() => _ = window.DispatchEventAsync(name, payload, sessionToken)));
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

    public ObservableCollection<MacroDefinition> Macros { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        ConfigurationPathText.Text = _configurationPath;
        MacrosList.ItemsSource = Macros;
        _pluginLoader.Load(Path.Combine(AppContext.BaseDirectory, "Plugins"), WriteLog);
        LoadConfiguration();
        _cleanConfiguration = JsonSerializer.Serialize(Macros, JsonOptions);
        if (Macros.Count > 0)
            MacrosList.SelectedIndex = 0;
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
        MacrosList.SelectedItem = macro;
        WriteLog($"Macro créée : {macro.Name} (événements : {macro.EventNamesDisplay}).");
    }

    private void RemoveMacro_Click(object sender, RoutedEventArgs e)
    {
        if (MacrosList.SelectedItem is not MacroDefinition macro)
            return;

        if (MessageBox.Show(this, $"Supprimer la macro « {macro.Name} » ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        Macros.Remove(macro);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros);
        ActionsList.Items.Refresh();
        if (Macros.Count > 0)
            MacrosList.SelectedIndex = 0;
        WriteLog($"Macro supprimée : {macro.Name}.");
    }

    private void EditMacro_Click(object sender, RoutedEventArgs e)
    {
        if (MacrosList.SelectedItem is not MacroDefinition macro)
            return;

        var updatedMacro = PromptForMacroDetails(macro);
        if (updatedMacro is null)
            return;

        macro.Name = updatedMacro.Value.Name;
        macro.SetEventNames(updatedMacro.Value.EventNames);
        RefreshSystemActionDescriptions(Macros);
        SelectedMacroNameText.Text = macro.Name;
        SelectedMacroEventText.Text = $"Déclencheurs : {macro.EventNamesDisplay}";
        WriteLog($"Macro modifiée : {macro.Name} (événements : {macro.EventNamesDisplay}).");
    }

    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        if (MacrosList.SelectedItem is not MacroDefinition macro)
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
            : PromptForAction(choice.Kind);
        if (action is null)
            return;

        macro.Actions.Add(action);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros);
        ActionsList.Items.Refresh();
        ActionsList.SelectedIndex = ActionsList.Items.Count - 1;
        WriteLog($"Action ajoutée à « {macro.Name} » : {action.DisplayName}.");
    }

    private async void EditAction_Click(object sender, RoutedEventArgs e)
    {
        if (MacrosList.SelectedItem is not MacroDefinition macro ||
            ActionsList.SelectedItem is not MacroAction action)
            return;

        var updatedAction = action.Kind == MacroActionKind.Plugin
            ? ConfigurePluginAction(action.PluginId, action.ActionId, action)
            : PromptForAction(action.Kind, action);
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
                await plugin.ActionChangedAsync(ResolvePluginAction(Macros, plugin, action),
                    ResolvePluginAction(Macros, plugin, updatedAction));
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
        ActionsList.Items.Refresh();
        ActionsList.SelectedItem = updatedAction;
        EditActionButton.IsEnabled = updatedAction.Kind != MacroActionKind.Beep;
        WriteLog($"Action modifiée dans « {macro.Name} » : {updatedAction.DisplayName}.");
    }

    private MacroAction? PromptForAction(MacroActionKind kind, MacroAction? existing = null)
    {
        if (IsSystemAction(kind))
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

    private static bool IsSystemAction(MacroActionKind kind) =>
        kind is MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
            MacroActionKind.EnableAction or MacroActionKind.DisableAction;

    private static string SystemActionName(MacroActionKind kind) => kind switch
    {
        MacroActionKind.EnableMacro => "Système — activer une macro",
        MacroActionKind.DisableMacro => "Système — désactiver une macro",
        MacroActionKind.EnableAction => "Système — activer une action",
        MacroActionKind.DisableAction => "Système — désactiver une action",
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

        var macros = JsonSerializer.Deserialize<ObservableCollection<MacroDefinition>>(cleanConfiguration, JsonOptions)
            ?? throw new JsonException("La référence de configuration est invalide.");
        var macro = macros.SingleOrDefault(item => item.Id == result.MacroId);
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
        return JsonSerializer.Serialize(macros, JsonOptions);
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
        foreach (var action in macros.SelectMany(macro => macro.Actions).Where(action => IsSystemAction(action.Kind)))
        {
            try
            {
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
        if (MacrosList.SelectedItem is not MacroDefinition macro ||
            ActionsList.SelectedItem is not MacroAction action)
            return;

        if (MessageBox.Show(this, $"Supprimer l'action « {action.DisplayName} » de la macro « {macro.Name} » ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        macro.Actions.Remove(action);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros);
        ActionsList.Items.Refresh();
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
            var host = new PluginHost(this, _sessionCancellation.Token);
            var provider = plugin as IPluginInstanceProvider;
            instanceId = existing?.InstanceId ?? instanceId;
            PluginActionData? data;
            if (provider is not null && provider.RequiresInstance(actionId))
            {
                if (string.IsNullOrEmpty(instanceId))
                    throw new InvalidOperationException("Cette action n'est associée à aucune configuration de plugin. Recréez-la depuis la liste des actions.");
                var configuration = FindInstanceConfiguration(Macros, plugin, provider, instanceId);
                data = provider.ConfigureInstanceAction(actionId,
                    existing is null ? null : ToPluginData(existing), ToPluginData(configuration), host);
            }
            else
            {
                data = plugin.ConfigureAction(actionId, existing is null ? null : ToPluginData(existing), host);
                if (provider is not null && provider.IsInstanceConfiguration(actionId))
                    instanceId ??= Guid.NewGuid().ToString();
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

    private static MacroAction FindInstanceConfiguration(
        IEnumerable<MacroDefinition> macros, IPlugin plugin, IPluginInstanceProvider provider, string instanceId)
    {
        var matches = macros.SelectMany(macro => macro.Actions)
            .Where(action => action.Kind == MacroActionKind.Plugin &&
                string.Equals(action.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase) &&
                provider.IsInstanceConfiguration(action.ActionId) &&
                string.Equals(action.InstanceId, instanceId, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException($"Configuration du plugin {plugin.Name} introuvable ou dupliquée.");
    }

    private static PluginActionData ResolvePluginAction(
        IEnumerable<MacroDefinition> macros, IPlugin plugin, MacroAction action)
    {
        var data = ToPluginData(action);
        if (plugin is IPluginInstanceProvider provider && provider.RequiresInstance(action.ActionId) &&
            !string.IsNullOrEmpty(action.InstanceId))
        {
            var configuration = FindInstanceConfiguration(macros, plugin, provider, action.InstanceId);
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
                action.PluginDisplayName = plugin.DescribeAction(ResolvePluginAction(macros, plugin, action));
                if (plugin is IPluginInstanceProvider provider && provider.RequiresInstance(action.ActionId) &&
                    !string.IsNullOrEmpty(action.InstanceId))
                {
                    var configuration = FindInstanceConfiguration(macros, plugin, provider, action.InstanceId);
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

    private async void RunMacro_Click(object sender, RoutedEventArgs e)
    {
        if (MacrosList.SelectedItem is MacroDefinition macro)
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
        if (JsonSerializer.Serialize(Macros, JsonOptions) == _cleanConfiguration)
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

    private void MacrosList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MacrosList.SelectedItem is MacroDefinition macro)
        {
            EditMacroButton.IsEnabled = true;
            SelectedMacroNameText.Text = macro.Name;
            SelectedMacroEventText.Text = $"Déclencheurs : {macro.EventNamesDisplay}";
            ActionsList.ItemsSource = macro.Actions;
            ActionsList.SelectedIndex = -1;
            EditActionButton.IsEnabled = false;
        }
        else
        {
            EditMacroButton.IsEnabled = false;
            SelectedMacroNameText.Text = "Sélectionnez une macro";
            SelectedMacroEventText.Text = "Choisissez une macro pour consulter ses actions.";
            ActionsList.ItemsSource = null;
            EditActionButton.IsEnabled = false;
        }
    }

    private void ActionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EditActionButton.IsEnabled = ActionsList.SelectedItem is MacroAction action &&
                                     action.Kind != MacroActionKind.Beep;
    }

    private void EditMacroItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroDefinition macro })
        {
            MacrosList.SelectedItem = macro;
            EditMacro_Click(sender, e);
        }
    }

    private void RemoveMacroItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroDefinition macro })
        {
            MacrosList.SelectedItem = macro;
            RemoveMacro_Click(sender, e);
        }
    }

    private void EditActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroAction action })
        {
            ActionsList.SelectedItem = action;
            EditAction_Click(sender, e);
        }
    }

    private void RemoveActionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroAction action })
        {
            ActionsList.SelectedItem = action;
            RemoveAction_Click(sender, e);
        }
    }

    private void MacroItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsItemControl(e.OriginalSource) || sender is not ListBoxItem { DataContext: MacroDefinition macro })
            return;
        MacrosList.SelectedItem = macro;
        e.Handled = true;
        EditMacro_Click(sender, e);
    }

    private void ActionItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsItemControl(e.OriginalSource) || sender is not ListBoxItem { DataContext: MacroAction action })
            return;
        ActionsList.SelectedItem = action;
        e.Handled = true;
        if (action.Kind == MacroActionKind.Beep)
        {
            MessageBox.Show(this, "Cette action n'a pas de paramètres à modifier.", "Modification impossible",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        EditAction_Click(sender, e);
    }

    private static bool IsItemControl(object source)
    {
        var current = source as DependencyObject;
        while (current is not null && current is not ListBoxItem)
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

    private async Task ExecuteMacroAsync(MacroDefinition macro, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        WriteLog($"Exécution de la macro « {macro.Name} ».");
        foreach (var actionId in macro.Actions.Select(action => action.Id).ToArray())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var action = macro.Actions.FirstOrDefault(item => item.Id == actionId);
            if (action is null || !action.IsEnabled)
                continue;

            try
            {
                switch (action.Kind)
                {
                    case MacroActionKind.Log:
                        WriteLog(action.Value);
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
                        await RunPowerShellAsync(action.Value, cancellationToken);
                        break;
                    case MacroActionKind.Plugin:
                        var plugin = _pluginLoader.Find(action.PluginId)
                            ?? throw new InvalidOperationException($"Plugin introuvable : {action.PluginId}");
                        await plugin.ExecuteActionAsync(ResolvePluginAction(Macros, plugin, action),
                            new PluginHost(this, cancellationToken), cancellationToken);
                        break;
                    case MacroActionKind.EnableMacro:
                    case MacroActionKind.DisableMacro:
                    case MacroActionKind.EnableAction:
                    case MacroActionKind.DisableAction:
                        var result = ExecuteSystemAction(Macros, action);
                        _cleanConfiguration = AcceptRuntimeStateChange(_cleanConfiguration, result);
                        WriteLog(result.Message);
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
                WriteLog($"Erreur dans « {macro.Name} » ({action.Kind}) : {exception.Message}");
            }
        }
        if (!cancellationToken.IsCancellationRequested)
            WriteLog($"Macro terminée : « {macro.Name} ».");
    }

    private void SaveConfiguration_Click(object sender, RoutedEventArgs e) => SaveConfiguration();

    private async void ReloadConfiguration_Click(object sender, RoutedEventArgs e)
    {
        ObservableCollection<MacroDefinition> newMacros;
        try
        {
            newMacros = ReadConfiguration();
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
        MacrosList.SelectedItem = null;
        ActionsList.ItemsSource = null;
        Macros.Clear();
        foreach (var macro in newMacros)
            Macros.Add(macro);
        _cleanConfiguration = JsonSerializer.Serialize(Macros, JsonOptions);
        if (Macros.Count > 0)
            MacrosList.SelectedIndex = 0;

        WriteLog($"Configuration rechargée : {Macros.Count} macro(s).");
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
            var savedMacros = ReadConfiguration();
            foreach (var macro in savedMacros)
                Macros.Add(macro);
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

    private ObservableCollection<MacroDefinition> ReadConfiguration()
    {
        if (!File.Exists(_configurationPath))
            return [CreateSampleMacro()];

        var json = File.ReadAllText(_configurationPath);
        var macros = JsonSerializer.Deserialize<ObservableCollection<MacroDefinition>>(json, JsonOptions)
            ?? throw new JsonException("Le fichier ne contient pas de configuration de macros.");
        foreach (var macro in macros)
        {
            if (macro.EventNames.Count == 0 || macro.EventNames.Any(string.IsNullOrWhiteSpace))
                throw new JsonException($"La macro « {macro.Name} » doit avoir au moins un événement non vide.");
        }
        foreach (var action in macros.SelectMany(macro => macro.Actions))
        {
            if (action.Kind is not (MacroActionKind.Log or MacroActionKind.Delay or MacroActionKind.Beep or
                MacroActionKind.ShowMessage or MacroActionKind.OpenUrl or MacroActionKind.PowerShell or MacroActionKind.Plugin or
                MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or MacroActionKind.EnableAction or MacroActionKind.DisableAction))
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

        MigratePluginInstances(macros, _pluginLoader.Plugins);
        RefreshPluginDescriptions(macros);
        ValidateEntityIds(macros);
        RefreshSystemActionDescriptions(macros);
        return macros;
    }

    private static void MigratePluginInstances(IEnumerable<MacroDefinition> macros, IEnumerable<IPlugin> plugins)
    {
        foreach (var instancePlugin in plugins)
        {
            if (instancePlugin is not IPluginInstanceProvider plugin)
                continue;
            var actions = macros.SelectMany(macro => macro.Actions)
                .Where(action => action.Kind == MacroActionKind.Plugin &&
                    string.Equals(action.PluginId, instancePlugin.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var configurations = actions.Where(action => plugin.IsInstanceConfiguration(action.ActionId)).ToArray();
            foreach (var configuration in configurations)
                if (string.IsNullOrEmpty(configuration.InstanceId))
                    configuration.InstanceId = Guid.NewGuid().ToString();
            foreach (var action in actions.Where(action => plugin.RequiresInstance(action.ActionId) &&
                                                           string.IsNullOrEmpty(action.InstanceId)))
            {
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
            var json = JsonSerializer.Serialize(Macros, JsonOptions);
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
        if (MacrosList.SelectedItem is not MacroDefinition macro)
        {
            MessageBox.Show(this, "Créez ou sélectionnez d'abord une macro.", "Aucune macro sélectionnée",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (macro.MatchesEvent(eventName))
        {
            MessageBox.Show(this, "Cet événement est déjà associé à la macro.", "Événement déjà présent",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, $"Ajouter « {eventName} » aux déclencheurs de « {macro.Name} » ?",
                "Ajouter un déclencheur", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        macro.EventNames.Add(eventName);
        SelectedMacroEventText.Text = $"Déclencheurs : {macro.EventNamesDisplay}";
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
        panel.Children.Add(new TextBlock { Text = "Événements déclencheurs (un seul suffit) :", Margin = new Thickness(0, 12, 0, 5) });
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
            if (string.IsNullOrWhiteSpace(nameBox.Text) || eventNames.Count == 0 ||
                eventNames.Any(string.IsNullOrWhiteSpace))
            {
                MessageBox.Show(dialog, "Le nom et au moins un événement déclencheur sont obligatoires.",
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

    private async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("-");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Impossible de démarrer PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

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
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new TimeoutException("Le script PowerShell a dépassé la limite de 30 secondes.");
        }

        var output = await outputTask;
        var errors = await errorTask;
        if (!string.IsNullOrWhiteSpace(output))
            WriteLog($"PowerShell : {output.TrimEnd()}");
        if (!string.IsNullOrWhiteSpace(errors))
            WriteLog($"PowerShell stderr : {errors.TrimEnd()}");
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"PowerShell s'est terminé avec le code {process.ExitCode}.");
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
            new(MacroActionKind.DisableAction, "Système — désactiver une action")
        };
        choices.AddRange(CreatePluginActionChoices(_pluginLoader.Plugins, Macros));
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
        IEnumerable<IPlugin> plugins, IEnumerable<MacroDefinition> macros)
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

                var configurations = macros.SelectMany(macro => macro.Actions)
                    .Where(config => config.Kind == MacroActionKind.Plugin &&
                        string.Equals(config.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase) &&
                        provider.IsInstanceConfiguration(config.ActionId) && !string.IsNullOrEmpty(config.InstanceId))
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