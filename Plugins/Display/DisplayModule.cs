using EventGhostDotNet.Plugins.Shared;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Plugins.Display;

public sealed class DisplayModule : IPlugin
{
    public string Id => "Display";
    public string Name => "Écran";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
        [new("DisplayInputSource", "Écran — changer l'entrée")];

    public PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host)
    {
        if (actionId != "DisplayInputSource")
            throw new InvalidOperationException($"Action écran inconnue : {actionId}");

        var monitorService = host.Monitors;
        if (monitorService is null)
        {
            MessageBox.Show(host.OwnerWindow(), "La gestion des écrans n'est pas disponible sur cette plateforme.",
                "Écran non pris en charge", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        IReadOnlyList<PlatformMonitor> monitors;
        try
        {
            monitors = monitorService.GetMonitors();
        }
        catch (Exception exception) when (exception is Win32Exception or DllNotFoundException or
                                           EntryPointNotFoundException or PlatformNotSupportedException)
        {
            MessageBox.Show(host.OwnerWindow(), $"Impossible d'énumérer les écrans DDC/CI.\n{exception.Message}",
                "Erreur d'écran", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        if (monitors.Count == 0)
        {
            MessageBox.Show(host.OwnerWindow(), "Aucun écran compatible DDC/CI n'a été détecté.",
                "Écran introuvable", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var dialog = new Window
        {
            Title = existing is null ? "Changer l'entrée du moniteur" : "Modifier l'entrée du moniteur",
            Owner = host.OwnerWindow(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(16)
        };
        var panel = new StackPanel { Width = 480 };
        panel.Children.Add(new TextBlock { Text = "Moniteur compatible DDC/CI :" });
        var selector = new ComboBox
        {
            ItemsSource = monitors,
            DisplayMemberPath = nameof(PlatformMonitor.Label),
            SelectedItem = existing is null ? monitors[0] :
                monitors.FirstOrDefault(monitor => string.Equals(monitor.Key, existing.Target, StringComparison.OrdinalIgnoreCase)),
            Margin = new Thickness(0, 5, 0, 12)
        };
        panel.Children.Add(selector);
        panel.Children.Add(new TextBlock { Text = "Code d'entrée VCP (décimal ou hexadécimal) :" });
        var input = new TextBox { Text = existing?.Value ?? "0x0F", Margin = new Thickness(0, 5, 0, 12) };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) });
        var save = new Button { Content = existing is null ? "Ajouter" : "Enregistrer", IsDefault = true, Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) =>
        {
            if (selector.SelectedItem is not PlatformMonitor || !TryParseInputSource(input.Text, out _))
            {
                MessageBox.Show(dialog, "Sélectionnez un moniteur et saisissez un code entre 0 et 255.",
                    "Configuration invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true && selector.SelectedItem is PlatformMonitor selected &&
               TryParseInputSource(input.Text, out var value)
            ? new PluginActionData(actionId, value.ToString(CultureInfo.InvariantCulture), selected.Key)
            : null;
    }

    public string DescribeAction(PluginActionData action) => $"Entrée écran : {action.Target} → {action.Value}";

    public Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        if (action.ActionId != "DisplayInputSource" || !uint.TryParse(action.Value, out var value))
            throw new InvalidOperationException($"Action ou source d'entrée invalide : {action.ActionId}, {action.Value}");
        cancellationToken.ThrowIfCancellationRequested();
        var monitorService = host.Monitors
            ?? throw new NotSupportedException("La gestion des écrans n'est pas disponible sur cette plateforme.");
        monitorService.SetInputSource(action.Target, value);
        host.Log($"Entrée du moniteur {action.Target} réglée sur {value}.");
        return Task.CompletedTask;
    }

    public Task ResetAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static bool TryParseInputSource(string value, out uint inputSource)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out inputSource)
                   && inputSource <= byte.MaxValue;
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSource)
               && inputSource <= byte.MaxValue;
    }
}
