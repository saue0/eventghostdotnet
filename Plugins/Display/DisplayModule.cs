using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EventGhostDotNet.PluginContracts;
using EventGhostDotNet.Plugins.Shared;

namespace EventGhostDotNet.Plugins.Display;

public sealed class DisplayModule : IPlugin
{
    private const byte VcpInputSource = 0x60;
    private const byte VcpBrightness = 0x10;
    private const byte VcpContrast = 0x12;

    private const string InputSourceAction = "DisplayInputSource";
    private const string BrightnessAction = "DisplayBrightness";
    private const string ContrastAction = "DisplayContrast";
    private const string ReadInputAction = "DisplayReadInput";

    public string Id => "Display";
    public string Name => "Écran";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } =
    [
        new(InputSourceAction, "Écran — changer l'entrée"),
        new(BrightnessAction, "Écran — luminosité"),
        new(ContrastAction, "Écran — contraste"),
        new(ReadInputAction, "Écran — lire l'entrée courante")
    ];

    public PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host)
    {
        var code = VcpCodeFor(actionId)
            ?? throw new InvalidOperationException($"Action écran inconnue : {actionId}");

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
            Title = (existing is null ? "Ajouter : " : "Modifier : ") + ActionTitle(actionId),
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

        TextBox? input = null;
        if (actionId != ReadInputAction)
        {
            var percentage = actionId is BrightnessAction or ContrastAction;
            panel.Children.Add(new TextBlock
            {
                Text = percentage ? "Valeur en pourcentage (0 à 100) :" : "Code d'entrée VCP (décimal ou hexadécimal) :"
            });
            input = new TextBox
            {
                Text = existing?.Value ?? (percentage ? "50" : "0x0F"),
                Margin = new Thickness(0, 5, 0, 12)
            };
            panel.Children.Add(input);
        }

        var currentValue = new TextBlock
        {
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(currentValue);

        void UpdateCurrent()
        {
            if (selector.SelectedItem is not PlatformMonitor monitor)
            {
                currentValue.Text = string.Empty;
                return;
            }

            try
            {
                var value = monitorService.GetVcpFeature(monitor.Key, code);
                currentValue.Text = actionId switch
                {
                    ReadInputAction or InputSourceAction =>
                        $"Entrée actuelle : {value.Current} (0x{value.Current:X2}).",
                    _ => $"Valeur actuelle : {value.Current} sur {value.Maximum}."
                };
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or
                                               DllNotFoundException or EntryPointNotFoundException)
            {
                currentValue.Text = $"Valeur actuelle indisponible : {exception.Message}";
            }
        }

        selector.SelectionChanged += (_, _) => UpdateCurrent();
        UpdateCurrent();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) });
        var save = new Button
        {
            Content = existing is null ? "Ajouter" : "Enregistrer",
            IsDefault = true,
            Padding = new Thickness(12, 6, 12, 6)
        };
        save.Click += (_, _) =>
        {
            if (selector.SelectedItem is not PlatformMonitor)
            {
                MessageBox.Show(dialog, "Sélectionnez un moniteur.", "Configuration invalide",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (input is not null && !TryParseValue(actionId, input.Text, out _))
            {
                MessageBox.Show(dialog,
                    actionId is BrightnessAction or ContrastAction
                        ? "Saisissez une valeur entre 0 et 100."
                        : "Saisissez un code entre 0 et 255.",
                    "Configuration invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        if (dialog.ShowDialog() != true || selector.SelectedItem is not PlatformMonitor selected)
            return null;
        if (input is null)
            return new PluginActionData(actionId, string.Empty, selected.Key);
        return TryParseValue(actionId, input.Text, out var parsed)
            ? new PluginActionData(actionId, parsed.ToString(CultureInfo.InvariantCulture), selected.Key)
            : null;
    }

    public string DescribeAction(PluginActionData action) => action.ActionId switch
    {
        InputSourceAction => $"Entrée écran : {action.Target} → {action.Value}",
        BrightnessAction => $"Luminosité écran : {action.Target} → {action.Value} %",
        ContrastAction => $"Contraste écran : {action.Target} → {action.Value} %",
        ReadInputAction => $"Lire l'entrée écran : {action.Target}",
        _ => $"Action écran : {action.ActionId}"
    };

    public Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var monitorService = host.Monitors
            ?? throw new NotSupportedException("La gestion des écrans n'est pas disponible sur cette plateforme.");

        switch (action.ActionId)
        {
            case InputSourceAction:
            {
                if (!TryParseInputSource(action.Value, out var inputSource))
                    throw new InvalidOperationException($"Source d'entrée invalide : {action.Value}");
                monitorService.SetInputSource(action.Target, inputSource);
                host.Log($"Entrée du moniteur {action.Target} réglée sur {inputSource}.");
                break;
            }
            case BrightnessAction:
                SetPercentage(monitorService, host, action, VcpBrightness, "Luminosité");
                break;
            case ContrastAction:
                SetPercentage(monitorService, host, action, VcpContrast, "Contraste");
                break;
            case ReadInputAction:
            {
                var current = monitorService.GetVcpFeature(action.Target, VcpInputSource);
                var description = $"Entrée actuelle : {current.Current} (0x{current.Current:X2})";
                host.Result = description;
                host.Log($"Entrée du moniteur {action.Target} : {current.Current} (0x{current.Current:X2}).");
                break;
            }
            default:
                throw new InvalidOperationException($"Action écran inconnue : {action.ActionId}");
        }

        return Task.CompletedTask;
    }

    public Task ResetAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void SetPercentage(
        IMonitorService monitors, IPluginHost host, PluginActionData action, byte code, string label)
    {
        if (!TryParsePercentage(action.Value, out var percent))
            throw new InvalidOperationException($"Pourcentage de {label.ToLowerInvariant()} invalide : {action.Value}");

        var current = monitors.GetVcpFeature(action.Target, code);
        var maximum = current.Maximum == 0 ? 100 : current.Maximum;
        var value = (uint)Math.Round(percent * maximum / 100.0, MidpointRounding.AwayFromZero);
        monitors.SetVcpFeature(action.Target, code, value);
        host.Log($"{label} du moniteur {action.Target} réglée sur {percent} % (valeur {value} sur {maximum}).");
    }

    private static byte? VcpCodeFor(string actionId) => actionId switch
    {
        InputSourceAction => VcpInputSource,
        ReadInputAction => VcpInputSource,
        BrightnessAction => VcpBrightness,
        ContrastAction => VcpContrast,
        _ => null
    };

    private static string ActionTitle(string actionId) => actionId switch
    {
        InputSourceAction => "changer l'entrée",
        BrightnessAction => "régler la luminosité",
        ContrastAction => "régler le contraste",
        ReadInputAction => "lire l'entrée courante",
        _ => "action écran"
    };

    private static bool TryParseValue(string actionId, string value, out uint parsed) =>
        actionId is BrightnessAction or ContrastAction
            ? TryParsePercentage(value, out parsed)
            : TryParseInputSource(value, out parsed);

    private static bool TryParsePercentage(string value, out uint percentage) =>
        uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out percentage) &&
        percentage <= 100;

    private static bool TryParseInputSource(string value, out uint inputSource)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out inputSource)
                   && inputSource <= byte.MaxValue;
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out inputSource)
               && inputSource <= byte.MaxValue;
    }
}
