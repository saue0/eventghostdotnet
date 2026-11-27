using System.Windows;
using System.Windows.Controls;
using EventGhostDotNet.PluginContracts;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private void EnsurePluginStartupConfiguration(string pluginId)
    {
        if (PluginStartups.Any(configuration =>
                string.Equals(configuration.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
            return;

        var plugin = _pluginRuntime.Find(pluginId);
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
            var plugin = _pluginRuntime.Find(configuration.PluginId);
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

    internal async void AddPluginStartup_Click(object sender, RoutedEventArgs e)
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
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectPluginStartupItem(configuration);
        WriteLog($"Démarrage ajouté : {configuration.DisplayName}.");
        await RestartConfiguredPluginsAsync();
    }

    private async void TogglePluginStartup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginStartupConfiguration configuration })
            return;
        configuration.IsEnabled = !configuration.IsEnabled;
        await ApplyPluginStartupStateAsync(configuration);
    }

    private async Task ApplyPluginStartupStateAsync(PluginStartupConfiguration configuration)
    {
        WriteLog($"Démarrage « {configuration.DisplayName} » {(configuration.IsEnabled ? "activé" : "désactivé")}.");
        await RestartConfiguredPluginsAsync();
    }

    internal async void EditPluginStartupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginStartupConfiguration configuration })
            return;
        await EditPluginStartupAsync(configuration);
    }

    private async Task EditPluginStartupAsync(PluginStartupConfiguration configuration)
    {
        var plugin = _pluginRuntime.Find(configuration.PluginId);
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
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        WriteLog($"Démarrage modifié : {configuration.DisplayName}.");
        await RestartConfiguredPluginsAsync();
    }

    internal async void RemovePluginStartupItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginStartupConfiguration configuration })
            return;

        var pluginActions = Macros.SelectMany(macro => macro.Actions).Concat(StartupActions)
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
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        WriteLog($"Démarrage supprimé : {configuration.DisplayName}.");
        await RestartConfiguredPluginsAsync();
    }

    private PluginStartupConfiguration? ConfigurePluginStartup(
        IPlugin plugin, PluginStartupConfiguration? existing)
    {
        try
        {
            var host = new PluginHost(this, CreateActionContext(_sessionCancellation.Token));
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
            WriteLog($"Erreur de configuration du démarrage {plugin.Id} : {exception.Message}", LogLevel.Error);
            MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private IPlugin? PromptForPluginStartupTarget()
    {
        var plugins = _pluginRuntime.Plugins.ToArray();
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
        if (RevealTreeItem(_startupSection) is not TreeViewItem parent)
            return;
        parent.IsExpanded = true;
        parent.UpdateLayout();
        if (parent.ItemContainerGenerator.ContainerFromItem(configuration) is TreeViewItem child)
        {
            child.IsSelected = true;
            child.BringIntoView();
        }
    }
}
