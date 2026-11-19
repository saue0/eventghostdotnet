using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EventGhostDotNet.PluginContracts;
using Forms = System.Windows.Forms;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListView = System.Windows.Controls.ListView;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace EventGhostDotNet.Plugins.SystemTrayMenu;

public sealed class SystemTrayMenuEntry : INotifyPropertyChanged
{
    private string _label = string.Empty;
    private string _eventName = string.Empty;
    private bool _isSeparator;

    public string Label
    {
        get => _label;
        set
        {
            if (_label == value)
                return;
            _label = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    public string EventName
    {
        get => _eventName;
        set
        {
            if (_eventName == value)
                return;
            _eventName = value;
            OnPropertyChanged();
        }
    }

    public bool IsSeparator
    {
        get => _isSeparator;
        set
        {
            if (_isSeparator == value)
                return;
            _isSeparator = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    [JsonIgnore]
    public string DisplayLabel => IsSeparator ? "— Séparateur —" : Label;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class SystemTrayMenuModule : IPlugin
{
    private const string StartupConfigurationId = "SystemTrayMenu";
    private const string ShowEventName = "System.Show";
    private const string HideEventName = "System.Hide";
    private const string ExitEventName = "System.Exit";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _contextMenu;

    public string Id => "SystemTrayMenu";
    public string Name => "System Tray Menu";
    public IReadOnlyList<PluginActionDescriptor> Actions { get; } = [];
    public bool HasStartupConfiguration => true;

    public PluginStartupData CreateDefaultStartupConfiguration() =>
        new(StartupConfigurationId, Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(CreateDefaultEntries(), JsonOptions), string.Empty);

    public PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host)
    {
        if (existing is null && host.GetPluginStartupConfigurations(Id).Count > 0)
        {
            WpfMessageBox.Show(host.Owner, "Une configuration System Tray Menu existe déjà.",
                Name, MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        ObservableCollection<SystemTrayMenuEntry> entries;
        try
        {
            entries = new ObservableCollection<SystemTrayMenuEntry>(
                existing is null ? CreateDefaultEntries() : ReadEntries(existing.Value));
        }
        catch (JsonException exception)
        {
            WpfMessageBox.Show(host.Owner, $"La configuration du menu est invalide.\n{exception.Message}",
                Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        if (!ShowConfigurationDialog(host.Owner, entries))
            return null;

        return new PluginStartupData(
            StartupConfigurationId,
            existing?.InstanceId ?? Guid.NewGuid().ToString(),
            JsonSerializer.Serialize(entries, JsonOptions),
            string.Empty);
    }

    public string DescribeStartup(PluginStartupData configuration)
    {
        try
        {
            var itemCount = ReadEntries(configuration.Value).Count(entry => !entry.IsSeparator);
            return $"{Name} — {itemCount} élément(s)";
        }
        catch (JsonException)
        {
            return $"{Name} — configuration invalide";
        }
    }

    public Task StartupAsync(
        PluginStartupData configuration, IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (configuration.ConfigurationId != StartupConfigurationId)
            throw new InvalidOperationException($"Configuration de démarrage inconnue : {configuration.ConfigurationId}");
        if (_notifyIcon is not null)
            throw new InvalidOperationException("Le menu de la zone de notification est déjà démarré.");

        var entries = ReadEntries(configuration.Value);
        ValidateEntries(entries);
        var menu = new Forms.ContextMenuStrip();
        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = Name,
            ContextMenuStrip = menu
        };

        try
        {
            foreach (var entry in entries)
            {
                if (entry.IsSeparator)
                {
                    menu.Items.Add(new Forms.ToolStripSeparator());
                    continue;
                }

                var eventName = entry.EventName;
                var menuItem = new Forms.ToolStripMenuItem(entry.Label);
                menuItem.Click += (_, _) =>
                {
                    host.Log($"System Tray Menu : événement « {eventName} » déclenché.");
                    host.RaiseEvent(eventName, null);
                };
                menu.Items.Add(menuItem);
            }

            icon.Visible = true;
            _contextMenu = menu;
            _notifyIcon = icon;
            host.Log($"{Name} démarré avec {entries.Count(entry => !entry.IsSeparator)} élément(s).");
        }
        catch
        {
            icon.Dispose();
            menu.Dispose();
            throw;
        }

        return Task.CompletedTask;
    }

    public PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public string DescribeAction(PluginActionData action) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"{Name} ne fournit pas d'actions de macro.");

    public Task ResetAsync()
    {
        var icon = _notifyIcon;
        var menu = _contextMenu;
        _notifyIcon = null;
        _contextMenu = null;

        try
        {
            if (icon is not null)
            {
                icon.Visible = false;
                icon.ContextMenuStrip = null;
            }
        }
        finally
        {
            icon?.Dispose();
            menu?.Dispose();
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await ResetAsync();

    private static List<SystemTrayMenuEntry> CreateDefaultEntries() =>
    [
        new()
        {
            Label = "Afficher EventGhost",
            EventName = ShowEventName
        },
        new()
        {
            Label = "Masquer EventGhost",
            EventName = HideEventName
        },
        new() { IsSeparator = true },
        new()
        {
            Label = "Sortie",
            EventName = ExitEventName
        }
    ];

    private static List<SystemTrayMenuEntry> ReadEntries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var entries = JsonSerializer.Deserialize<List<SystemTrayMenuEntry>>(json, JsonOptions) ?? [];
        foreach (var entry in entries.Where(entry => entry is not null))
        {
            entry.EventName = entry.EventName switch
            {
                "EGDN.SystemTray.Show" => ShowEventName,
                "EGDN.SystemTray.Hide" => HideEventName,
                "EGDN.SystemTray.Exit" => ExitEventName,
                _ => entry.EventName
            };
        }
        ValidateEntries(entries);
        return entries;
    }

    private static void ValidateEntries(IEnumerable<SystemTrayMenuEntry?> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is null)
                throw new JsonException("Une entrée du menu est nulle.");
            if (!entry.IsSeparator &&
                (string.IsNullOrWhiteSpace(entry.Label) || string.IsNullOrWhiteSpace(entry.EventName)))
                throw new JsonException("Chaque élément de menu doit avoir un libellé et un nom d'événement.");
        }
    }

    private static bool ShowConfigurationDialog(Window owner, ObservableCollection<SystemTrayMenuEntry> entries)
    {
        var dialog = new Window
        {
            Title = "System Tray Menu",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 650,
            Height = 500,
            MinWidth = 560,
            MinHeight = 420,
            Padding = new Thickness(12)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var description = new TextBlock
        {
            Text = "Choisissez les éléments du menu et l'événement à déclencher lors d'un clic.",
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(description, 0);
        Grid.SetColumnSpan(description, 2);
        layout.Children.Add(description);

        var list = new WpfListView { ItemsSource = entries, Margin = new Thickness(0, 0, 8, 8) };
        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = "Libellé",
            DisplayMemberBinding = new WpfBinding(nameof(SystemTrayMenuEntry.DisplayLabel)),
            Width = 250
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Événement",
            DisplayMemberBinding = new WpfBinding(nameof(SystemTrayMenuEntry.EventName)),
            Width = 280
        });
        list.View = view;
        Grid.SetRow(list, 1);
        Grid.SetColumn(list, 0);
        layout.Children.Add(list);

        var label = new WpfTextBox { Margin = new Thickness(0, 0, 0, 6), IsEnabled = false };
        var eventName = new WpfTextBox { Margin = new Thickness(0, 0, 0, 6), IsEnabled = false };
        var fields = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddField(fields, 0, "Libellé :", label);
        AddField(fields, 1, "Événement :", eventName);
        Grid.SetRow(fields, 2);
        Grid.SetColumnSpan(fields, 2);
        layout.Children.Add(fields);

        void UpdateEditor()
        {
            var selected = list.SelectedItem as SystemTrayMenuEntry;
            var canEdit = selected is not null && !selected.IsSeparator;
            label.IsEnabled = canEdit;
            eventName.IsEnabled = canEdit;
            label.Text = canEdit ? selected!.Label : string.Empty;
            eventName.Text = canEdit ? selected!.EventName : string.Empty;
        }

        list.SelectionChanged += (_, _) => UpdateEditor();
        label.TextChanged += (_, _) =>
        {
            if (list.SelectedItem is SystemTrayMenuEntry { IsSeparator: false } selected)
                selected.Label = label.Text;
        };
        eventName.TextChanged += (_, _) =>
        {
            if (list.SelectedItem is SystemTrayMenuEntry { IsSeparator: false } selected)
                selected.EventName = eventName.Text;
        };

        var buttons = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var addItem = new WpfButton { Content = "Élément de menu", Margin = new Thickness(0, 0, 0, 4) };
        addItem.Click += (_, _) =>
        {
            var entry = new SystemTrayMenuEntry { Label = "Nouvelle entrée", EventName = "Menu.Selected" };
            entries.Add(entry);
            list.SelectedItem = entry;
            list.ScrollIntoView(entry);
        };
        var addSeparator = new WpfButton { Content = "Séparateur", Margin = new Thickness(0, 0, 0, 4) };
        addSeparator.Click += (_, _) =>
        {
            var entry = new SystemTrayMenuEntry { IsSeparator = true };
            entries.Add(entry);
            list.SelectedItem = entry;
            list.ScrollIntoView(entry);
        };
        var delete = new WpfButton { Content = "Supprimer", Margin = new Thickness(0, 12, 0, 4), IsEnabled = false };
        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is not SystemTrayMenuEntry selected)
                return;
            var index = entries.IndexOf(selected);
            entries.Remove(selected);
            if (entries.Count > 0)
                list.SelectedIndex = Math.Min(index, entries.Count - 1);
        };
        var moveUp = new WpfButton { Content = "Monter", IsEnabled = false };
        var moveDown = new WpfButton { Content = "Descendre", IsEnabled = false };
        moveUp.Click += (_, _) => MoveSelected(-1);
        moveDown.Click += (_, _) => MoveSelected(1);
        buttons.Children.Add(addItem);
        buttons.Children.Add(addSeparator);
        buttons.Children.Add(delete);
        buttons.Children.Add(moveUp);
        buttons.Children.Add(moveDown);
        Grid.SetRow(buttons, 1);
        Grid.SetColumn(buttons, 1);
        layout.Children.Add(buttons);

        void MoveSelected(int offset)
        {
            if (list.SelectedItem is not SystemTrayMenuEntry selected)
                return;
            var index = entries.IndexOf(selected);
            var newIndex = index + offset;
            if (newIndex < 0 || newIndex >= entries.Count)
                return;
            entries.Move(index, newIndex);
            list.SelectedItem = selected;
        }

        list.SelectionChanged += (_, _) =>
        {
            var index = list.SelectedIndex;
            delete.IsEnabled = index >= 0;
            moveUp.IsEnabled = index > 0;
            moveDown.IsEnabled = index >= 0 && index < entries.Count - 1;
        };

        var dialogButtons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        dialogButtons.Children.Add(new WpfButton { Content = "Annuler", IsCancel = true, MinWidth = 90 });
        var save = new WpfButton { Content = "OK", IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) =>
        {
            try
            {
                ValidateEntries(entries);
                if (!entries.Any(entry => !entry.IsSeparator))
                    throw new JsonException("Ajoutez au moins un élément de menu.");
                dialog.DialogResult = true;
            }
            catch (JsonException exception)
            {
                WpfMessageBox.Show(dialog, exception.Message, "Configuration invalide",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        dialogButtons.Children.Add(save);
        Grid.SetRow(dialogButtons, 3);
        Grid.SetColumnSpan(dialogButtons, 2);
        layout.Children.Add(dialogButtons);

        dialog.Content = layout;
        UpdateEditor();
        return dialog.ShowDialog() == true;
    }

    private static void AddField(Grid grid, int row, string caption, WpfTextBox textBox)
    {
        var label = new TextBlock
        {
            Text = caption,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
    }
}
