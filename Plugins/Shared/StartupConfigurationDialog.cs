using System.Windows;
using System.Windows.Controls;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Plugins.Shared;

internal sealed record StartupConfigurationField(string Label, string Value);

internal static class StartupConfigurationDialog
{
    public static string[]? Show(
        Window owner,
        string title,
        IReadOnlyList<StartupConfigurationField> fields,
        Func<string[], string?> validate)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(18)
        };
        var panel = new StackPanel { Width = 480 };
        var inputs = new List<TextBox>();

        foreach (var field in fields)
        {
            panel.Children.Add(new TextBlock
            {
                Text = field.Label,
                Margin = new Thickness(0, 0, 0, 5)
            });
            var input = new TextBox
            {
                Text = field.Value,
                Margin = new Thickness(0, 0, 0, 12)
            };
            inputs.Add(input);
            panel.Children.Add(input);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true, MinWidth = 90 });
        var save = new Button
        {
            Content = "Enregistrer",
            IsDefault = true,
            MinWidth = 90,
            Margin = new Thickness(8, 0, 0, 0)
        };
        string[]? result = null;
        save.Click += (_, _) =>
        {
            var values = inputs.Select(input => input.Text.Trim()).ToArray();
            var error = validate(values);
            if (error is not null)
            {
                MessageBox.Show(dialog, error, "Configuration invalide",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = values;
            dialog.DialogResult = true;
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? result : null;
    }
}

internal sealed class StartupTaskGroup
{
    private sealed record Entry(CancellationTokenSource Cancellation, Task Task);

    private readonly List<Entry> _entries = [];

    public void Start(
        Func<CancellationToken, Task> operation,
        CancellationToken sessionToken,
        IPluginHost host,
        string description)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        var task = RunLoggedAsync(operation, cancellation.Token, host, description);
        _entries.Add(new Entry(cancellation, task));
    }

    public async Task StopAsync()
    {
        var entries = _entries.ToArray();
        foreach (var entry in entries)
            entry.Cancellation.Cancel();

        try
        {
            await Task.WhenAll(entries.Select(entry => entry.Task));
        }
        finally
        {
            foreach (var entry in entries)
                entry.Cancellation.Dispose();
            _entries.Clear();
        }
    }

    private static async Task RunLoggedAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        IPluginHost host,
        string description)
    {
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            host.Log($"Erreur du plugin ({description}) : {exception.Message}");
            throw;
        }
    }
}
