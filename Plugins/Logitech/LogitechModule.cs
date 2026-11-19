using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using EventGhostDotNet.PluginContracts;
using Microsoft.VisualBasic;

namespace EventGhostDotNet.Plugins.Logitech;

public sealed class LogitechModule : IPlugin, IPluginInstanceProvider
{
    private sealed record Instance(LogitechUnifyClient Client, string Prefix);
    private sealed record Target(string Path, string Prefix)
    {
        public string Label => $"{Prefix} — {Path}";
    }

    private readonly Dictionary<string, Instance> _clients = new(StringComparer.OrdinalIgnoreCase);
    public string Id => "Logitech";
    public string Name => "Logitech";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new("LogitechChangeHost", "Logitech — changer d'hôte"),
        new("LogitechSendRaw", "Logitech — envoyer un rapport brut")
    ];

    public bool HasStartupConfiguration => true;

    public PluginStartupData CreateDefaultStartupConfiguration() =>
        new("LogitechStart", Guid.NewGuid().ToString(), string.Empty, string.Empty);

    public PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host)
    {
        var existingAction = existing is null
            ? null
            : new PluginActionData(existing.ConfigurationId, existing.Value, existing.Target);
        var configured = ConfigureStart(existingAction, host);
        return configured is null
            ? null
            : new PluginStartupData("LogitechStart", existing?.InstanceId ?? Guid.NewGuid().ToString(),
                configured.Value, configured.Target);
    }

    public string DescribeStartup(PluginStartupData configuration) =>
        string.IsNullOrWhiteSpace(configuration.Value)
            ? "Logitech — démarrage"
            : $"Logitech — {configuration.Value}";

    public Task StartupAsync(
        PluginStartupData configuration, IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (configuration.ConfigurationId != "LogitechStart")
            throw new InvalidOperationException($"Configuration de démarrage Logitech inconnue : {configuration.ConfigurationId}");
        StartListening(configuration.Value, configuration.Target, host);
        return Task.CompletedTask;
    }

    public PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host)
    {
        if (actionId is not ("LogitechChangeHost" or "LogitechSendRaw"))
            throw new InvalidOperationException($"Action Logitech inconnue : {actionId}");

        var target = SelectTarget(existing, host);
        if (target is null)
            return null;
        return ConfigureCommand(actionId, existing, target, host);
    }

    public bool IsInstanceConfiguration(string actionId) => actionId == "LogitechStart";

    public bool RequiresInstance(string actionId) => actionId is "LogitechChangeHost" or "LogitechSendRaw";

    public string GetInstanceName(PluginActionData configuration) => configuration.Value;

    public bool MatchesInstance(PluginActionData action, PluginActionData configuration) =>
        string.IsNullOrWhiteSpace(action.Target) ||
        string.Equals(action.Target, configuration.Target, StringComparison.OrdinalIgnoreCase);

    public PluginActionData BindInstance(PluginActionData action, PluginActionData configuration) =>
        action with { Target = configuration.Target };

    public PluginActionData? ConfigureInstanceAction(
        string actionId, PluginActionData? existing, PluginActionData configuration, IPluginHost host)
    {
        if (!RequiresInstance(actionId))
            throw new InvalidOperationException($"Action Logitech inconnue : {actionId}");
        return ConfigureCommand(actionId, existing, configuration.Target, host);
    }

    private static PluginActionData? ConfigureCommand(
        string actionId, PluginActionData? existing, string target, IPluginHost host)
    {
        var hostAction = actionId == "LogitechChangeHost";
        var value = Interaction.InputBox(
            hostAction ? "Octets de l'hôte en hexadécimal :" : "Rapport HID en hexadécimal (max. 20 octets) :",
            hostAction ? "Logitech — changer d'hôte" : "Logitech — commande HID",
            existing?.Value ?? (hostAction ? "01" : "11 01 00 0D"));
        if (string.IsNullOrEmpty(value))
            return null;
        var compact = new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray());
        var maxLength = hostAction ? 32 : 40;
        if (compact.Length == 0 || compact.Length > maxLength || compact.Length % 2 != 0 ||
            compact.Any(character => !Uri.IsHexDigit(character)))
        {
            MessageBox.Show(host.Owner, hostAction
                ? "L'hôte doit contenir de 1 à 16 octets hexadécimaux."
                : "Le rapport doit contenir de 1 à 20 octets hexadécimaux.",
                "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return new PluginActionData(actionId, value, target);
    }

    public string DescribeAction(PluginActionData action) => action.ActionId switch
    {
        "LogitechChangeHost" => $"Logitech — changer d'hôte : {action.Value}",
        "LogitechSendRaw" => $"Logitech — envoyer HID : {action.Value}",
        _ => throw new InvalidOperationException($"Action Logitech inconnue : {action.ActionId}")
    };

    public async Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action.ActionId)
        {
            case "LogitechChangeHost":
                await GetClient(action.Target).ChangeHostAsync(action.Value);
                break;
            case "LogitechSendRaw":
                await GetClient(action.Target).SendRawAsync(action.Value);
                break;
            default:
                throw new InvalidOperationException($"Action Logitech inconnue : {action.ActionId}");
        }
    }

    public async Task ResetAsync()
    {
        var clients = _clients.Values.ToArray();
        _clients.Clear();
        await Task.WhenAll(clients.Select(instance => instance.Client.DisposeAsync().AsTask()));
    }

    private void StartListening(string prefix, string target, IPluginHost host)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(prefix))
            throw new InvalidOperationException("Le récepteur et le préfixe d'événement Logitech sont obligatoires.");
        if (_clients.TryGetValue(target, out var current))
        {
            if (current.Prefix == prefix)
            {
                host.Log($"L'écoute Logitech « {prefix} » est déjà démarrée.");
                return;
            }
            throw new InvalidOperationException($"Le récepteur Logitech est déjà utilisé avec le préfixe « {current.Prefix} ».");
        }
        var client = new LogitechUnifyClient(
            new LogitechReceiver(target, target), prefix, host.RaiseEvent, host.Log);
        client.Start();
        _clients.Add(target, new Instance(client, prefix));
        host.Log($"Écoute Logitech démarrée — événements préfixés par « {prefix} ».");
    }

    public async ValueTask DisposeAsync() => await ResetAsync();

    private LogitechUnifyClient GetClient(string target)
    {
        if (!string.IsNullOrWhiteSpace(target) && _clients.TryGetValue(target, out var instance))
            return instance.Client;
        if (string.IsNullOrWhiteSpace(target) && _clients.Count == 1)
            return _clients.Values.Single().Client;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(target)
            ? "Sélectionnez un récepteur Logitech pour cette action."
            : "L'écoute Logitech du récepteur configuré n'est pas démarrée.");
    }

    private static PluginActionData? ConfigureStart(PluginActionData? existing, IPluginHost host)
    {
        IReadOnlyList<LogitechReceiver> receivers;
        try
        {
            receivers = LogitechUnifyClient.FindReceivers();
        }
        catch (Exception exception) when (exception is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            MessageBox.Show(host.Owner, $"Impossible de rechercher les récepteurs Logitech.\n{exception.Message}",
                "Erreur HID", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        if (receivers.Count == 0)
        {
            MessageBox.Show(host.Owner, "Aucun récepteur Logitech Unifying ou Bolt compatible n'a été détecté.",
                "Récepteur introuvable", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        var dialog = CreateDialog(host.Owner, "Configurer le récepteur Logitech");
        var panel = new StackPanel { Width = 560 };
        panel.Children.Add(new TextBlock { Text = "Récepteur HID :" });
        var selector = new ComboBox
        {
            ItemsSource = receivers,
            DisplayMemberPath = nameof(LogitechReceiver.Label),
            SelectedItem = existing is null ? receivers[0] :
                receivers.FirstOrDefault(receiver => string.Equals(receiver.Path, existing.Target, StringComparison.OrdinalIgnoreCase)),
            Margin = new Thickness(0, 5, 0, 12)
        };
        panel.Children.Add(selector);
        panel.Children.Add(new TextBlock { Text = "Préfixe des événements :" });
        var prefix = new TextBox { Text = existing?.Value ?? "Logitech", Margin = new Thickness(0, 5, 0, 12) };
        panel.Children.Add(prefix);
        AddButtons(dialog, panel, () =>
        {
            if (selector.SelectedItem is LogitechReceiver && !string.IsNullOrWhiteSpace(prefix.Text))
                return true;
            MessageBox.Show(dialog, "Sélectionnez un récepteur et saisissez un préfixe d'événement.",
                "Configuration incomplète", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        });
        return dialog.ShowDialog() == true && selector.SelectedItem is LogitechReceiver receiver
            ? new PluginActionData("LogitechStart", prefix.Text.Trim(), receiver.Path)
            : null;
    }

    private static string? SelectTarget(PluginActionData? existing, IPluginHost host)
    {
        var targets = host.GetPluginStartupConfigurations("Logitech")
            .Where(configuration => configuration.ConfigurationId == "LogitechStart" &&
                             !string.IsNullOrWhiteSpace(configuration.Target) &&
                             !string.IsNullOrWhiteSpace(configuration.Value))
            .GroupBy(configuration => configuration.Target, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Target(group.Key, group.First().Value))
            .ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(host.Owner, "Ajoutez d'abord un démarrage Logitech dans la section « Démarrage ».",
                "Aucun récepteur configuré", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        var dialog = CreateDialog(host.Owner, "Choisir le récepteur Logitech");
        var panel = new StackPanel { Width = 560 };
        panel.Children.Add(new TextBlock { Text = "Récepteur associé à cette action :" });
        var selector = new ComboBox
        {
            ItemsSource = targets,
            DisplayMemberPath = nameof(Target.Label),
            SelectedItem = existing is null || string.IsNullOrEmpty(existing.Target) ? targets[0] :
                targets.FirstOrDefault(target => string.Equals(target.Path, existing.Target, StringComparison.OrdinalIgnoreCase)),
            Margin = new Thickness(0, 5, 0, 12)
        };
        panel.Children.Add(selector);
        AddButtons(dialog, panel, () =>
        {
            if (selector.SelectedItem is Target)
                return true;
            MessageBox.Show(dialog, "Sélectionnez un récepteur Logitech.", "Récepteur requis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        });
        return dialog.ShowDialog() == true && selector.SelectedItem is Target selected ? selected.Path : null;
    }

    private static Window CreateDialog(Window owner, string title) => new()
    {
        Title = title, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Padding = new Thickness(16)
    };

    private static void AddButtons(Window dialog, StackPanel panel, Func<bool> validate)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) });
        var save = new Button { Content = "Continuer", IsDefault = true, Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) => { if (validate()) dialog.DialogResult = true; };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
    }
}
