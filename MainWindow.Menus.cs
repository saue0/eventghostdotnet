using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Globalization;
using EventGhostDotNet.PluginContracts;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace EventGhostDotNet;

public partial class MainWindow
{
    private const int MaximumUndoHistory = 100;
    private readonly Stack<string> _undoConfiguration = [];
    private readonly Stack<string> _redoConfiguration = [];
    private readonly HashSet<ConfigurationItem> _historyItems =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyCollectionChanged> _historyCollections =
        new(ReferenceEqualityComparer.Instance);
    private string _lastHistorySnapshot = string.Empty;
    private bool _historyCapturePending;
    private bool _restoringHistory;
    private bool _autoExpandOnEvent = true;
    private string _lastFindText = string.Empty;
    private int _lastFindIndex = -1;

    private sealed record ClipboardPayload(
        string Type, string Value, IReadOnlyList<string>? Children = null, string? SourceId = null,
        IReadOnlyList<ClipboardPayload>? Groups = null, bool IsEnabled = true);
    private sealed record ClipboardEvent(string Name, bool IsEnabled);
    private sealed record SearchTarget(string Label, Action Select);

    private void InitializeMenuState()
    {
        RefreshHistorySubscriptions();
        _lastHistorySnapshot = SerializeConfiguration();
        UpdateMenuSelectionState();
    }

    private bool ShouldShowLogEntry(EventLogEntry entry) =>
        entry.Category switch
        {
            LogCategory.Macro when ShowMacrosInLogMenuItem.IsChecked != true => false,
            LogCategory.Action when ShowActionsInLogMenuItem.IsChecked != true => false,
            _ => entry.Level switch
            {
                LogLevel.Debug => LogDebugFilter.IsChecked == true,
                LogLevel.Information => LogInformationFilter.IsChecked == true,
                LogLevel.Warning => LogWarningFilter.IsChecked == true,
                LogLevel.Error => LogErrorFilter.IsChecked == true,
                _ => false
            }
        };

    private void LogCategoryFilter_Click(object sender, RoutedEventArgs e)
    {
        RefreshLogView();
        PersistDisplaySettings();
    }

    private void ShowDebugLog_Click(object sender, RoutedEventArgs e)
    {
        LogDebugFilter.IsChecked = ShowDebugLogMenuItem.IsChecked;
        RefreshLogView();
        PersistDisplaySettings();
    }

    private void LogDisplayOption_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _logEntries)
            entry.RefreshDisplay(ShowLogTimeMenuItem.IsChecked == true,
                DateStampLogMenuItem.IsChecked == true, IndentLogMenuItem.IsChecked == true);
        RefreshLogView();
        PersistDisplaySettings();
    }

    private void RefreshLogView()
    {
        _logView?.Refresh();
        ScrollLogToEnd();
    }

    private void ToolbarVisibility_Click(object sender, RoutedEventArgs e)
    {
        MainToolBarTray.Visibility = ToolbarVisibilityMenuItem.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        PersistDisplaySettings();
    }

    private void AutoExpandOnEvent_Click(object sender, RoutedEventArgs e)
    {
        _autoExpandOnEvent = AutoExpandOnEventMenuItem.IsChecked == true;
        PersistDisplaySettings();
    }

    private void ExpandSelected_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTreeItemExpanded(true);

    private void CollapseSelected_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTreeItemExpanded(false);

    private void SetSelectedTreeItemExpanded(bool isExpanded)
    {
        if (ConfigurationTree.SelectedItem is not { } selected)
            return;
        ConfigurationTree.UpdateLayout();
        if (FindTreeItemByDataContext(selected) is { } treeItem)
            treeItem.IsExpanded = isExpanded;
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => SetConfigurationTreeExpanded(true);

    private void CollapseAll_Click(object sender, RoutedEventArgs e) => SetConfigurationTreeExpanded(false);

    private void SetConfigurationTreeExpanded(bool isExpanded)
    {
        _startupSection.IsExpanded = isExpanded;
        foreach (var group in MacroGroups)
        {
            group.IsExpanded = isExpanded;
            foreach (var macro in group.Macros)
                macro.IsExpanded = isExpanded;
        }

        ConfigurationTree.UpdateLayout();
        for (var index = 0; index < ConfigurationTree.Items.Count; index++)
            if (ConfigurationTree.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem item)
                SetTreeItemExpandedRecursively(item, isExpanded);
        SynchronizeHistoryViewBaseline();
    }

    private static void SetTreeItemExpandedRecursively(TreeViewItem item, bool isExpanded)
    {
        item.IsExpanded = isExpanded;
        item.UpdateLayout();
        for (var index = 0; index < item.Items.Count; index++)
            if (item.ItemContainerGenerator.ContainerFromIndex(index) is TreeViewItem child)
                SetTreeItemExpandedRecursively(child, isExpanded);
    }

    private TreeViewItem? FindTreeItemByDataContext(object dataContext)
    {
        for (var index = 0; index < ConfigurationTree.Items.Count; index++)
        {
            if (ConfigurationTree.ItemContainerGenerator.ContainerFromIndex(index) is not TreeViewItem item)
                continue;
            if (ReferenceEquals(item.DataContext, dataContext))
                return item;
            if (FindTreeItemByDataContext(item, dataContext) is { } descendant)
                return descendant;
        }
        return null;
    }

    private static TreeViewItem? FindTreeItemByDataContext(TreeViewItem parent, object dataContext)
    {
        for (var index = 0; index < parent.Items.Count; index++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(index) is not TreeViewItem item)
                continue;
            if (ReferenceEquals(item.DataContext, dataContext))
                return item;
            if (FindTreeItemByDataContext(item, dataContext) is { } descendant)
                return descendant;
        }
        return null;
    }

    private void ExpandMacroForEvent(MacroDefinition macro)
    {
        if (!_autoExpandOnEvent)
            return;
        var group = FindMacroGroup(macro);
        if (group is null)
            return;
        group.IsExpanded = true;
        macro.IsExpanded = true;
        if (RevealTreeItem(group) is { } groupItem)
            groupItem.IsExpanded = true;
        if (FindTreeItemByDataContext(macro) is { } macroItem)
            macroItem.IsExpanded = true;
    }

    private void AddMacroFolder_Click(object sender, RoutedEventArgs e)
    {
        AddMacroGroup("Ajouter un dossier", "Nouveau dossier");
    }

    private void AddMacroEvent_Click(object sender, RoutedEventArgs e)
    {
        var macro = ConfigurationTree.SelectedItem switch
        {
            MacroDefinition selectedMacro => selectedMacro,
            MacroEvent macroEvent => macroEvent.Macro,
            MacroAction action => Macros.FirstOrDefault(item => item.Actions.Contains(action)),
            _ => _selectedMacro
        };
        if (macro is null)
        {
            MessageBox.Show(this, "Sélectionnez d'abord une macro.", "Aucune macro sélectionnée",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var eventName = PromptForText("Ajouter un événement", "Nom exact de l'événement :", string.Empty)?.Trim();
        if (string.IsNullOrWhiteSpace(eventName))
            return;
        if (macro.EventNames.Contains(eventName, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Cet événement est déjà associé à la macro.", "Événement déjà présent",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        macro.EventNames.Add(eventName);
        SelectTreeItem(macro);
        WriteLog($"Événement « {eventName} » ajouté à la macro « {macro.Name} ».");
    }

    private void EditSelected_Click(object sender, RoutedEventArgs e)
    {
        switch (ConfigurationTree.SelectedItem)
        {
            case MacroGroup group:
                RenameMacroGroup(group);
                break;
            case MacroDefinition:
                EditMacro_Click(sender, e);
                break;
            case MacroEvent macroEvent:
                EditEvent(macroEvent);
                break;
            case MacroAction:
                EditAction_Click(sender, e);
                break;
            case PluginStartupConfiguration startup:
                EditPluginStartupItem_Click(new Button { DataContext = startup }, e);
                break;
        }
    }

    private void RenameSelected_Click(object sender, RoutedEventArgs e)
    {
        switch (ConfigurationTree.SelectedItem)
        {
            case MacroGroup group:
                RenameMacroGroup(group);
                break;
            case MacroDefinition macro:
                RenameMacro(macro);
                break;
            case MacroEvent macroEvent:
                EditEvent(macroEvent);
                break;
        }
    }

    private void RenameMacro(MacroDefinition macro)
    {
        var name = PromptForText("Renommer la macro", "Nom de la macro :", macro.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        macro.Name = name;
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectTreeItem(macro);
        WriteLog($"Macro renommée : {macro.Name}.");
    }

    private async void ToggleSelectedEnabled_Click(object sender, RoutedEventArgs e)
    {
        switch (ConfigurationTree.SelectedItem)
        {
            case MacroGroup group:
                group.IsEnabled = !group.IsEnabled;
                WriteLog($"Groupe « {group.Name} » {(group.IsEnabled ? "activé" : "désactivé")}.");
                break;
            case MacroDefinition macro:
                macro.IsEnabled = !macro.IsEnabled;
                WriteLog($"Macro « {macro.Name} » {(macro.IsEnabled ? "activée" : "désactivée")}.");
                break;
            case MacroEvent macroEvent:
                macroEvent.IsEnabled = !macroEvent.IsEnabled;
                WriteLog($"Événement « {macroEvent.Name} » {(macroEvent.IsEnabled ? "activé" : "désactivé")}.");
                break;
            case MacroAction action:
                action.IsEnabled = !action.IsEnabled;
                WriteLog($"Action « {action.DisplayName} » {(action.IsEnabled ? "activée" : "désactivée")}.");
                break;
            case PluginStartupConfiguration startup:
                startup.IsEnabled = !startup.IsEnabled;
                await ApplyPluginStartupStateAsync(startup);
                break;
        }
        UpdateMenuSelectionState();
    }

    private void UpdateMenuSelectionState()
    {
        if (!IsInitialized)
            return;

        var selected = ConfigurationTree.SelectedItem;
        var canCopy = selected is MacroGroup or MacroDefinition or MacroEvent or MacroAction or PluginStartupConfiguration;
        CutMenuItem.IsEnabled = canCopy;
        CopyMenuItem.IsEnabled = canCopy;
        DeleteMenuItem.IsEnabled = canCopy;
        UndoMenuItem.IsEnabled = _undoConfiguration.Count > 0;
        RedoMenuItem.IsEnabled = _redoConfiguration.Count > 0;
        ToggleSelectedEnabledMenuItem.IsEnabled = selected is MacroGroup or MacroDefinition or MacroEvent or MacroAction or PluginStartupConfiguration;
        PropertiesMenuItem.IsEnabled = selected is MacroGroup or MacroDefinition or MacroEvent or MacroAction ||
                                       selected is PluginStartupConfiguration { CanConfigure: true };
        RenameSelectedMenuItem.IsEnabled = selected is MacroGroup or MacroDefinition or MacroEvent;
        RunSelectedMenuItem.IsEnabled = selected is MacroDefinition or MacroEvent or MacroAction;
        ToggleSelectedEnabledMenuItem.IsChecked = selected switch
        {
            MacroGroup group => !group.IsEnabled,
            MacroDefinition macro => !macro.IsEnabled,
            MacroEvent macroEvent => !macroEvent.IsEnabled,
            MacroAction action => !action.IsEnabled,
            PluginStartupConfiguration startup => !startup.IsEnabled,
            _ => false
        };
        CopyAsPythonMenuItem.IsEnabled = selected is MacroDefinition or MacroAction;
        try
        {
            PasteMenuItem.IsEnabled = System.Windows.Clipboard.ContainsText();
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            PasteMenuItem.IsEnabled = false;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var focusedElement = Keyboard.FocusedElement;
        if (focusedElement is TextBoxBase || focusedElement is ComboBox { IsEditable: true })
            return;
        if (LogList.IsKeyboardFocusWithin && e.Key is Key.X or Key.V)
            return;

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.P:
                    AddMacroGroup_Click(this, new RoutedEventArgs());
                    break;
                case Key.N:
                    AddMacroFolder_Click(this, new RoutedEventArgs());
                    break;
                case Key.M:
                    AddMacro_Click(this, new RoutedEventArgs());
                    break;
                case Key.E:
                    AddMacroEvent_Click(this, new RoutedEventArgs());
                    break;
                case Key.A:
                    AddAction_Click(this, new RoutedEventArgs());
                    break;
                default:
                    return;
            }
        }
        else if (modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    Undo_Click(this, new RoutedEventArgs());
                    break;
                case Key.Y:
                    Redo_Click(this, new RoutedEventArgs());
                    break;
                case Key.D:
                    ToggleSelectedEnabled_Click(this, new RoutedEventArgs());
                    break;
                case Key.L:
                    ClearLog_Click(this, new RoutedEventArgs());
                    break;
                case Key.F:
                    Find_Click(this, new RoutedEventArgs());
                    break;
                case Key.X:
                    CutSelection_Click(this, new RoutedEventArgs());
                    break;
                case Key.C when !LogList.IsKeyboardFocusWithin:
                    CopySelection_Click(this, new RoutedEventArgs());
                    break;
                case Key.V:
                    PasteSelection_Click(this, new RoutedEventArgs());
                    break;
                default:
                    return;
            }
        }
        else if (modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.F2:
                    if (!RenameSelectedMenuItem.IsEnabled)
                        return;
                    RenameSelected_Click(this, new RoutedEventArgs());
                    break;
                case Key.F3:
                    FindNext_Click(this, new RoutedEventArgs());
                    break;
                case Key.F5:
                    if (!RunSelectedMenuItem.IsEnabled)
                        return;
                    RunMacro_Click(this, new RoutedEventArgs());
                    break;
                case Key.Enter:
                    if (!PropertiesMenuItem.IsEnabled)
                        return;
                    EditSelected_Click(this, new RoutedEventArgs());
                    break;
                case Key.Delete:
                    DeleteSelection_Click(this, new RoutedEventArgs());
                    break;
                default:
                    return;
            }
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    private void RefreshHistorySubscriptions()
    {
        var items = new HashSet<ConfigurationItem>(ReferenceEqualityComparer.Instance);
        var collections = new HashSet<INotifyCollectionChanged>(ReferenceEqualityComparer.Instance);

        void ObserveCollection(INotifyCollectionChanged collection) => collections.Add(collection);
        void ObserveItem(ConfigurationItem item)
        {
            if (!items.Add(item))
                return;
            if (item is MacroGroup group)
            {
                ObserveCollection(group.Macros);
                ObserveCollection(group.Groups);
                foreach (var child in group.Groups)
                    ObserveItem(child);
                foreach (var macro in group.Macros)
                    ObserveItem(macro);
            }
            else if (item is MacroDefinition macro)
            {
                ObserveCollection(macro.EventNames);
                ObserveCollection(macro.DisabledEventNames);
                ObserveCollection(macro.Actions);
                foreach (var child in macro.GetChildren())
                    ObserveItem(child);
            }
            else if (item is ConfigurationContainer container)
            {
                foreach (var child in container.GetChildren())
                    ObserveItem(child);
            }
        }

        ObserveCollection(MacroGroups);
        ObserveCollection(_configurationRoot.Groups);
        ObserveCollection(PluginStartups);
        ObserveCollection(StartupActions);
        foreach (var group in MacroGroups)
            ObserveItem(group);
        foreach (var startup in PluginStartups)
            ObserveItem(startup);
        foreach (var action in StartupActions)
            ObserveItem(action);
        foreach (var oldItem in _historyItems.Except(items).ToArray())
            oldItem.PropertyChanged -= HistoryItem_PropertyChanged;
        foreach (var item in items.Except(_historyItems))
            item.PropertyChanged += HistoryItem_PropertyChanged;
        foreach (var oldCollection in _historyCollections.Except(collections).ToArray())
            oldCollection.CollectionChanged -= HistoryCollection_CollectionChanged;
        foreach (var collection in collections.Except(_historyCollections))
            collection.CollectionChanged += HistoryCollection_CollectionChanged;
        _historyItems.Clear();
        _historyItems.UnionWith(items);
        _historyCollections.Clear();
        _historyCollections.UnionWith(collections);
    }

    private void HistoryItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigurationItem.Parent) or nameof(MacroGroup.IsExpanded) or
            nameof(MacroDefinition.IsExpanded) or nameof(PluginStartupConfiguration.DisplayName) or
            nameof(PluginStartupConfiguration.CanConfigure))
            return;
        QueueHistoryCapture();
    }

    private void HistoryCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshHistorySubscriptions();
        QueueHistoryCapture();
    }

    private void QueueHistoryCapture()
    {
        if (_restoringHistory || _historyCapturePending)
            return;
        _historyCapturePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _historyCapturePending = false;
            CaptureConfigurationHistory();
        }));
    }

    private void CaptureConfigurationHistory()
    {
        if (_restoringHistory)
            return;
        var current = SerializeConfiguration();
        if (string.Equals(current, _lastHistorySnapshot, StringComparison.Ordinal))
            return;
        _undoConfiguration.Push(_lastHistorySnapshot);
        TrimHistory(_undoConfiguration);
        _redoConfiguration.Clear();
        _lastHistorySnapshot = current;
        UpdateMenuSelectionState();
    }

    private static void TrimHistory(Stack<string> history)
    {
        if (history.Count <= MaximumUndoHistory)
            return;
        var retained = history.Take(MaximumUndoHistory).Reverse().ToArray();
        history.Clear();
        foreach (var snapshot in retained)
            history.Push(snapshot);
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        CaptureConfigurationHistory();
        await RestoreHistoryAsync(_undoConfiguration, _redoConfiguration);
    }

    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        CaptureConfigurationHistory();
        await RestoreHistoryAsync(_redoConfiguration, _undoConfiguration);
    }

    private async Task RestoreHistoryAsync(Stack<string> source, Stack<string> destination)
    {
        if (source.Count == 0)
            return;
        var current = SerializeConfiguration();
        var startupExpanded = _startupSection.IsExpanded;
        var expandedGroups = MacroGroups.ToDictionary(group => group.Id, group => group.IsExpanded);
        var expandedMacros = Macros.ToDictionary(macro => macro.Id, macro => macro.IsExpanded);
        var target = source.Pop();
        destination.Push(current);
        _restoringHistory = true;
        var restored = false;
        try
        {
            ConfigurationDocument configuration;
            try
            {
                configuration = ReadConfiguration(target);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                WriteLog($"Impossible de restaurer la configuration : {exception.Message}", LogLevel.Error);
                MessageBox.Show(this, exception.Message, "Erreur d'historique",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await using (await _pluginRuntime.EnterAsync())
            {
                if (_isClosed)
                    return;
                await StartNewSessionAsync();
                MacroGroups.Clear();
                Macros.Clear();
                PluginStartups.Clear();
                StartupActions.Clear();
                _startupWindowMode = configuration.StartupWindowMode;
                _powerShellPath = configuration.PowerShellPath;
                foreach (var startup in configuration.PluginStartups)
                    PluginStartups.Add(startup);
                foreach (var action in configuration.StartupActions)
                    StartupActions.Add(action);
                foreach (var group in configuration.MacroGroups)
                    if (expandedGroups.TryGetValue(group.Id, out var groupIsExpanded))
                        group.IsExpanded = groupIsExpanded;
                foreach (var macro in configuration.Macros)
                    if (expandedMacros.TryGetValue(macro.Id, out var macroIsExpanded))
                        macro.IsExpanded = macroIsExpanded;
                LoadMacroCollections(configuration);
                SetStartupSectionExpanded(startupExpanded);
                RefreshPluginStartupDescriptions();
                RefreshPluginDescriptions(Macros);
                RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
                RefreshHistorySubscriptions();
                _lastHistorySnapshot = SerializeConfiguration();
                SelectStartupSection();
                restored = true;
                try
                {
                    await StartConfiguredPluginsAsync();
                }
                catch (AggregateException exception)
                {
                    WriteLog($"Certains plugins n'ont pas pu redémarrer : {exception.Message}", LogLevel.Error);
                    MessageBox.Show(this, exception.Message, "Erreur de démarrage de plugin",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            if (restored)
                WriteLog("Configuration annulée ou rétablie.");
        }
        catch (Exception exception) when (exception is OperationCanceledException or AggregateException)
        {
            WriteLog($"Impossible de restaurer la configuration : {exception.Message}", LogLevel.Error);
            MessageBox.Show(this, exception.Message, "Erreur d'historique",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _restoringHistory = false;
            if (!restored)
            {
                destination.Pop();
                source.Push(target);
            }
            UpdateMenuSelectionState();
        }
    }

    private void CopySelection_Click(object sender, RoutedEventArgs e)
    {
        CopySelectionToClipboard();
    }

    private bool CopySelectionToClipboard()
    {
        var payload = CreateClipboardPayload(ConfigurationTree.SelectedItem);
        if (payload is null)
            return false;
        try
        {
            System.Windows.Clipboard.SetText(JsonSerializer.Serialize(payload, JsonOptions));
            return true;
        }
        catch (ExternalException exception)
        {
            ReportClipboardError("Impossible de copier l'élément sélectionné.", exception);
            return false;
        }
    }

    private void CutSelection_Click(object sender, RoutedEventArgs e)
    {
        if (CopySelectionToClipboard())
            DeleteSelection_Click(sender, e);
    }

    private void DeleteSelection_Click(object sender, RoutedEventArgs e)
    {
        switch (ConfigurationTree.SelectedItem)
        {
            case MacroGroup group:
                RemoveMacroGroupItem_Click(new Button { DataContext = group }, e);
                break;
            case MacroDefinition macro:
                SelectTreeItem(macro);
                RemoveMacro_Click(sender, e);
                break;
            case MacroEvent macroEvent:
                RemoveEvent(macroEvent);
                break;
            case MacroAction action:
                RemoveActionItem_Click(new Button { DataContext = action }, e);
                break;
            case PluginStartupConfiguration startup:
                RemovePluginStartupItem_Click(new Button { DataContext = startup }, e);
                break;
        }
    }

    private ClipboardPayload? CreateClipboardPayload(object? selected)
    {
        return selected switch
        {
            MacroGroup group => new ClipboardPayload("Group", group.Name,
                group.Macros.Select(macro => JsonSerializer.Serialize(macro, JsonOptions)).ToArray(),
                SourceId: group.Id.ToString(),
                Groups: group.Groups.Select(child => CreateClipboardPayload(child)!).ToArray(),
                IsEnabled: group.IsEnabled),
            MacroDefinition macro => new ClipboardPayload("Macro", JsonSerializer.Serialize(macro, JsonOptions)),
            MacroEvent macroEvent => new ClipboardPayload("Event",
                JsonSerializer.Serialize(new ClipboardEvent(macroEvent.Name, macroEvent.IsEnabled), JsonOptions)),
            MacroAction action => new ClipboardPayload("Action", JsonSerializer.Serialize(action, JsonOptions),
                SourceId: StartupActions.Contains(action) ? "Startup" : GetOwnerMacro(action)?.Id.ToString()),
            PluginStartupConfiguration startup =>
                new ClipboardPayload("PluginStartup", JsonSerializer.Serialize(startup, JsonOptions)),
            _ => null
        };
    }

    private async void PasteSelection_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return;
            text = System.Windows.Clipboard.GetText();
        }
        catch (ExternalException exception)
        {
            ReportClipboardError("Impossible d'accéder au presse-papiers.", exception);
            return;
        }

        ClipboardPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ClipboardPayload>(text, JsonOptions);
        }
        catch (JsonException)
        {
            MessageBox.Show(this, "Le presse-papiers ne contient pas un élément EventGhost .NET.",
                "Collage impossible", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (payload is null)
            return;

        try
        {
            switch (payload.Type)
            {
                case "Group":
                    PasteGroup(payload);
                    break;
                case "Macro":
                    PasteMacro(payload);
                    break;
                case "Event":
                    PasteEvent(payload);
                    break;
                case "Action":
                    await PasteActionAsync(payload);
                    break;
                case "PluginStartup":
                    await PastePluginStartupAsync(payload);
                    break;
                default:
                    MessageBox.Show(this, "Le type d'élément du presse-papiers n'est pas pris en charge.",
                        "Collage impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            WriteLog($"Collage impossible : {exception.Message}", LogLevel.Warning);
            MessageBox.Show(this, exception.Message, "Collage impossible",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PasteGroup(ClipboardPayload payload)
    {
        var parsedGroups = new List<(ClipboardPayload Payload, List<MacroDefinition> Macros)>();
        void ReadGroups(ClipboardPayload source)
        {
            if (source.Type != "Group")
                throw new JsonException("Un sous-groupe copié est invalide.");
            var groupMacros = new List<MacroDefinition>();
            foreach (var macroJson in source.Children ?? [])
                groupMacros.Add(JsonSerializer.Deserialize<MacroDefinition>(macroJson, JsonOptions)
                    ?? throw new JsonException("Une macro copiée est vide."));
            parsedGroups.Add((source, groupMacros));
            foreach (var child in source.Groups ?? [])
                ReadGroups(child);
        }
        ReadGroups(payload);
        var macros = parsedGroups.SelectMany(item => item.Macros).ToList();
        var groupIds = parsedGroups
            .Where(item => Guid.TryParse(item.Payload.SourceId, out _))
            .ToDictionary(item => Guid.Parse(item.Payload.SourceId!), _ => Guid.NewGuid());
        var macroIds = macros.ToDictionary(macro => macro.Id, _ => Guid.NewGuid());
        var actionIds = macros.SelectMany(macro => macro.Actions)
            .ToDictionary(action => action.Id, _ => Guid.NewGuid());
        var nextGroup = 0;
        MacroGroup CreateGroup(ClipboardPayload source, MacroGroup? parent)
        {
            var groupMacros = parsedGroups[nextGroup++].Macros;
            var group = new MacroGroup
            {
                Id = Guid.TryParse(source.SourceId, out var sourceId) && groupIds.TryGetValue(sourceId, out var newId)
                    ? newId : Guid.NewGuid(),
                Name = MakeUniqueGroupName(source.Value),
                IsEnabled = source.IsEnabled
            };
            (parent?.Groups ?? _configurationRoot.Groups).Add(group);
            SynchronizeGroupOrder();
            foreach (var macro in groupMacros)
            {
                macro.Id = macroIds[macro.Id];
                foreach (var action in macro.Actions)
                    action.Id = actionIds[action.Id];
                RemapMacroActionTargets(macro, macroIds, actionIds, groupIds);
                macro.Name = MakeUniqueMacroName(macro.Name);
                AddMacroToGroup(macro, group);
            }
            foreach (var child in source.Groups ?? [])
                CreateGroup(child, group);
            return group;
        }
        var group = CreateGroup(payload, GetSelectedMacroGroup());
        SynchronizeMacroOrder();
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectMacroGroup(group);
        WriteLog($"Dossier collé : {group.Name}.");
    }

    private void PasteMacro(ClipboardPayload payload)
    {
        var macro = JsonSerializer.Deserialize<MacroDefinition>(payload.Value, JsonOptions)
            ?? throw new JsonException("La macro copiée est vide.");
        var group = GetSelectedMacroGroup() ?? MacroGroups.FirstOrDefault();
        if (group is null)
            throw new InvalidOperationException("Aucun groupe de macros n'est disponible.");
        RegenerateMacroIds(macro);
        macro.Name = MakeUniqueMacroName(macro.Name);
        AddMacroToGroup(macro, group);
        SynchronizeMacroOrder();
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        SelectTreeItem(macro);
        WriteLog($"Macro collée : {macro.Name}.");
    }

    private void PasteEvent(ClipboardPayload payload)
    {
        var macro = GetSelectedMacroForEditing();
        if (macro is null)
        {
            MessageBox.Show(this, "Sélectionnez une macro pour y coller l'événement.",
                "Collage impossible", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var eventItem = JsonSerializer.Deserialize<ClipboardEvent>(payload.Value, JsonOptions)
            ?? throw new JsonException("L'événement copié est vide.");
        if (string.IsNullOrWhiteSpace(eventItem.Name))
            throw new InvalidOperationException("Le nom de l'événement copié est vide.");
        if (macro.EventNames.Contains(eventItem.Name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Cet événement est déjà présent dans la macro.",
                "Collage impossible", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        macro.EventNames.Add(eventItem.Name);
        if (!eventItem.IsEnabled)
            macro.DisabledEventNames.Add(eventItem.Name);
        SelectTreeItem(macro);
        WriteLog($"Événement « {eventItem.Name} » collé dans « {macro.Name} ».");
    }

    private async Task PasteActionAsync(ClipboardPayload payload)
    {
        var action = JsonSerializer.Deserialize<MacroAction>(payload.Value, JsonOptions)
            ?? throw new JsonException("L'action copiée est vide.");
        var target = GetSelectedMacroForEditing();
        if (target is null)
        {
            MessageBox.Show(this, "Sélectionnez une macro ou la section Démarrage pour y coller l'action.",
                "Collage impossible", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sourceActionId = action.Id;
        action.Id = Guid.NewGuid();
        var targetId = ReferenceEquals(target, _startupMacro) ? "Startup" : target.Id.ToString();
        var requiresSameMacro = action.Kind == MacroActionKind.IfJump
            ? GetConditionalJumpTargetIds(action).Length > 0
            : action.Kind == MacroActionKind.Jump && action.JumpMode == JumpMode.GotoAction;
        if (requiresSameMacro &&
            !string.Equals(payload.SourceId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Une action qui saute vers une action de la macro ne peut être collée que dans sa macro d'origine.",
                "Collage impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (action.Kind == MacroActionKind.Jump && action.JumpMode == JumpMode.GotoAction &&
            Guid.TryParse(action.Value, out var jumpTargetId) && jumpTargetId == sourceActionId)
            action.Value = action.Id.ToString();
        if (action.Kind == MacroActionKind.IfJump)
        {
            if ((action.IfBranchMode ?? ConditionalJumpMode.GotoAction) == ConditionalJumpMode.GotoAction &&
                Guid.TryParse(action.Target, out var ifTargetId) && ifTargetId == sourceActionId)
                action.Target = action.Id.ToString();
            if ((action.ElseBranchMode ?? ConditionalJumpMode.GotoAction) == ConditionalJumpMode.GotoAction &&
                Guid.TryParse(action.ElseTarget, out var elseTargetId) && elseTargetId == sourceActionId)
                action.ElseTarget = action.Id.ToString();
        }
        if ((action.Kind is MacroActionKind.EnableAction or MacroActionKind.DisableAction) &&
            Guid.TryParse(action.Target, out var targetMacroId) && targetMacroId == target.Id &&
            Guid.TryParse(action.Value, out var targetActionId) && targetActionId == sourceActionId)
            action.Value = action.Id.ToString();
        if (action.Kind is MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem &&
            action.ActivationTarget == ActivationTargetKind.Action &&
            string.Equals(action.Target, targetId, StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(action.Value, out var unifiedTargetId) && unifiedTargetId == sourceActionId)
            action.Value = action.Id.ToString();
        var startupCount = PluginStartups.Count;
        target.Actions.Add(action);
        if (action.Kind == MacroActionKind.Plugin)
            EnsurePluginStartupConfiguration(action.PluginId);
        RefreshPluginDescriptions(Macros);
        RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
        if (ReferenceEquals(target, _startupMacro))
            SelectStartupActionItem(action);
        else
            SelectTreeItem(target, action);
        WriteLog($"Action collée dans « {target.Name} » : {action.DisplayName}.");
        if (PluginStartups.Count != startupCount && IsLoaded)
            await RestartConfiguredPluginsAsync();
    }

    private async Task PastePluginStartupAsync(ClipboardPayload payload)
    {
        var startup = JsonSerializer.Deserialize<PluginStartupConfiguration>(payload.Value, JsonOptions)
            ?? throw new JsonException("Le démarrage de plugin copié est vide.");
        startup.InstanceId = Guid.NewGuid().ToString();
        PluginStartups.Add(startup);
        RefreshPluginStartupDescriptions();
        SelectPluginStartupItem(startup);
        WriteLog($"Démarrage de plugin collé : {startup.DisplayName}.");
        if (IsLoaded)
            await RestartConfiguredPluginsAsync();
    }

    private static void RegenerateMacroIds(MacroDefinition macro)
    {
        var macroIds = new Dictionary<Guid, Guid> { [macro.Id] = Guid.NewGuid() };
        var actionIds = macro.Actions.ToDictionary(action => action.Id, _ => Guid.NewGuid());
        macro.Id = macroIds.Single().Value;
        foreach (var action in macro.Actions)
            action.Id = actionIds[action.Id];
        RemapMacroActionTargets(macro, macroIds, actionIds);
    }

    private static void RemapMacroActionTargets(
        MacroDefinition macro, IReadOnlyDictionary<Guid, Guid> macroIds, IReadOnlyDictionary<Guid, Guid> actionIds,
        IReadOnlyDictionary<Guid, Guid>? groupIds = null)
    {
        foreach (var action in macro.Actions)
        {
            if (action.Kind is MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
                MacroActionKind.EnableAction or MacroActionKind.DisableAction ||
                action.Kind is MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem &&
                action.ActivationTarget is ActivationTargetKind.Macro or ActivationTargetKind.Event or ActivationTargetKind.Action ||
                action.Kind == MacroActionKind.Jump && action.JumpMode != JumpMode.GotoAction)
            {
                if (Guid.TryParse(action.Target, out var targetMacroId) &&
                    macroIds.TryGetValue(targetMacroId, out var mappedMacroId))
                    action.Target = mappedMacroId.ToString();
            }
            if (action.Kind is MacroActionKind.EnableAction or MacroActionKind.DisableAction ||
                action.Kind is MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem &&
                action.ActivationTarget == ActivationTargetKind.Action ||
                action.Kind == MacroActionKind.Jump && action.JumpMode == JumpMode.GotoAction)
            {
                if (Guid.TryParse(action.Value, out var targetActionId) &&
                    actionIds.TryGetValue(targetActionId, out var mappedActionId))
                    action.Value = mappedActionId.ToString();
            }
            if (action.Kind is MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem &&
                action.ActivationTarget == ActivationTargetKind.Group && groupIds is not null &&
                Guid.TryParse(action.Target, out var targetGroupId) &&
                groupIds.TryGetValue(targetGroupId, out var mappedGroupId))
                action.Target = mappedGroupId.ToString();
            if (action.Kind == MacroActionKind.IfJump)
            {
                if (Guid.TryParse(action.Target, out var ifTargetId) &&
                    actionIds.TryGetValue(ifTargetId, out var mappedIfTargetId))
                    action.Target = mappedIfTargetId.ToString();
                if (Guid.TryParse(action.ElseTarget, out var elseTargetId) &&
                    actionIds.TryGetValue(elseTargetId, out var mappedElseTargetId))
                    action.ElseTarget = mappedElseTargetId.ToString();
            }
        }
    }

    private MacroDefinition? GetOwnerMacro(MacroAction action)
    {
        if (Macros.FirstOrDefault(macro => macro.Actions.Contains(action)) is { } macro)
            return macro;
        return StartupActions.Contains(action) ? _startupMacro : null;
    }

    private string MakeUniqueGroupName(string name)
    {
        var candidate = $"{name} - copie";
        for (var index = 2; MacroGroups.Any(group =>
                 string.Equals(group.Name, candidate, StringComparison.OrdinalIgnoreCase)); index++)
            candidate = $"{name} - copie ({index})";
        return candidate;
    }

    private string MakeUniqueMacroName(string name)
    {
        var candidate = $"{name} - copie";
        for (var index = 2; Macros.Any(macro =>
                 string.Equals(macro.Name, candidate, StringComparison.OrdinalIgnoreCase)); index++)
            candidate = $"{name} - copie ({index})";
        return candidate;
    }

    private MacroDefinition? GetSelectedMacroForEditing() => ConfigurationTree.SelectedItem switch
    {
        MacroDefinition macro => macro,
        MacroEvent macroEvent => macroEvent.Macro,
        MacroAction action => Macros.FirstOrDefault(macro => macro.Actions.Contains(action)) ??
                              (StartupActions.Contains(action) ? _startupMacro : null),
        StartupSection => _startupMacro,
        _ => _selectedMacro
    };

    private void CopyAsPython_Click(object sender, RoutedEventArgs e)
    {
        var python = ConfigurationTree.SelectedItem switch
        {
            MacroDefinition macro => ExportMacroAsPython(macro),
            MacroAction action => ExportActionsAsPython([action]),
            _ => null
        };
        if (python is null)
        {
            MessageBox.Show(this, "Sélectionnez une macro ou une action à exporter.",
                "Export Python", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(python);
        }
        catch (ExternalException exception)
        {
            ReportClipboardError("Impossible de copier le script Python.", exception);
        }
    }

    private static string ExportMacroAsPython(MacroDefinition macro)
    {
        var name = new string(macro.Name.Select(character =>
            char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        if (string.IsNullOrEmpty(name) || char.IsDigit(name[0]))
            name = $"macro_{name}";
        return ExportActionsAsPython(macro.Actions, name);
    }

    private static string ExportActionsAsPython(IEnumerable<MacroAction> actions, string functionName = "action")
    {
        var lines = new List<string>
        {
            "# Script Python exporté depuis EventGhost .NET",
            "import subprocess",
            "import time",
            "import webbrowser",
            "import winsound",
            "from tkinter import messagebox",
            "",
            $"def {functionName}():"
        };
        var hasAction = false;
        foreach (var action in actions)
        {
            if (!action.IsEnabled)
                continue;
            hasAction = true;
            switch (action.Kind)
            {
                case MacroActionKind.Log:
                    lines.Add($"    print({JsonSerializer.Serialize(action.Value)})");
                    break;
                case MacroActionKind.Delay when int.TryParse(action.Value, out var milliseconds) && milliseconds >= 0:
                    lines.Add($"    time.sleep({(milliseconds / 1000.0).ToString(CultureInfo.InvariantCulture)})");
                    break;
                case MacroActionKind.Beep:
                    lines.Add("    winsound.MessageBeep()");
                    break;
                case MacroActionKind.ShowMessage:
                    lines.Add($"    messagebox.showinfo({JsonSerializer.Serialize(functionName)}, {JsonSerializer.Serialize(action.Value)})");
                    break;
                case MacroActionKind.AskYesNo:
                    lines.Add($"    result = 'Oui' if messagebox.askyesno({JsonSerializer.Serialize(action.Target)}, {JsonSerializer.Serialize(action.Value)}) else 'Non'");
                    break;
                case MacroActionKind.OpenUrl:
                    lines.Add($"    webbrowser.open({JsonSerializer.Serialize(action.Value)})");
                    break;
                case MacroActionKind.PowerShell:
                    lines.Add($"    subprocess.run(['powershell', '-NoProfile', '-Command', {JsonSerializer.Serialize(action.Value)}], check=True)");
                    break;
                case MacroActionKind.Stop:
                    lines.Add("    return");
                    break;
                default:
                    lines.Add($"    # À convertir manuellement : {action.Kind} — {action.DisplayName}");
                    break;
            }
        }
        if (!hasAction)
            lines.Add("    pass");
        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        var text = PromptForText("Chercher dans la configuration", "Texte à rechercher :", _lastFindText);
        if (string.IsNullOrWhiteSpace(text))
            return;
        _lastFindText = text;
        _lastFindIndex = -1;
        FindNextMatch();
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastFindText))
        {
            Find_Click(sender, e);
            return;
        }
        FindNextMatch();
    }

    private void FindNextMatch()
    {
        var matches = GetSearchTargets()
            .Where(target => target.Label.Contains(_lastFindText, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            MessageBox.Show(this, $"Aucun élément ne contient « {_lastFindText} ».",
                "Recherche", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _lastFindIndex = (_lastFindIndex + 1) % matches.Length;
        matches[_lastFindIndex].Select();
    }

    private IEnumerable<SearchTarget> GetSearchTargets()
    {
        foreach (var startup in PluginStartups)
            yield return new SearchTarget(startup.DisplayName, () => SelectPluginStartupItem(startup));
        foreach (var action in StartupActions)
            yield return new SearchTarget(action.DisplayName, () => SelectStartupActionItem(action));
        foreach (var group in MacroGroups)
        {
            yield return new SearchTarget(group.Name, () => SelectMacroGroup(group));
            foreach (var macro in group.Macros)
            {
                yield return new SearchTarget(macro.Name, () => SelectTreeItem(macro));
                foreach (var eventItem in macro.EventItems)
                    yield return new SearchTarget(eventItem.Name, () => SelectTreeItem(macro, eventItem));
                foreach (var action in macro.Actions)
                    yield return new SearchTarget(action.DisplayName, () => SelectTreeItem(macro, action));
            }
        }
    }

    private void ReportClipboardError(string message, ExternalException exception)
    {
        WriteLog($"{message} {exception.Message}", LogLevel.Error);
        MessageBox.Show(this, $"{message}\n{exception.Message}", "Erreur du presse-papiers",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void SynchronizeHistoryViewBaseline()
    {
        if (!_historyCapturePending && !_restoringHistory)
            _lastHistorySnapshot = SerializeConfiguration();
    }
}
