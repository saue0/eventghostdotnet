using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Interop;
using Microsoft.VisualBasic;
using EventGhostDotNet.Core.Configuration;
using EventGhostDotNet.Core.Logging;
using EventGhostDotNet.Core.Macros;
using EventGhostDotNet.Core.PowerShell;
using EventGhostDotNet.PluginContracts;
using EventGhostDotNet.Platform.Windows;
using Brushes = System.Windows.Media.Brushes;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace EventGhostDotNet;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsLastSiblingProperty =
        DependencyProperty.RegisterAttached("IsLastSibling", typeof(bool), typeof(MainWindow),
            new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty IsTreeRootProperty =
        DependencyProperty.RegisterAttached("IsTreeRoot", typeof(bool), typeof(MainWindow),
            new FrameworkPropertyMetadata(false));

    private const string ApplicationStartupEventName = "System.Startup";
    private const string ConfigurationLoadedEventName = "System.ConfigurationLoaded";
    private const string SystemShowEventName = "System.Show";
    private const string SystemHideEventName = "System.Hide";
    private const string SystemExitEventName = "System.Exit";
    private const int MaximumLogIndentLevel = 64;

    private sealed record ActionChoice(
        MacroActionKind Kind, string Name, string? PluginId = null, string? ActionId = null, string? InstanceId = null)
    {
        public string Source => PluginId ?? "Système";
    }

    private enum LogCategory
    {
        General,
        Macro,
        Action
    }

    private sealed class EventLogEntry : INotifyPropertyChanged
    {
        private readonly DateTime _timestamp;
        private readonly string _message;
        private readonly string _symbol;
        private readonly string _label;
        private readonly AnsiLogFormatter _formatter;
        private readonly LogCategory _category;
        private readonly int _indentLevel;
        private TextBlock _display = new();
        private string _text = string.Empty;

        public EventLogEntry(DateTime timestamp, string message, string? eventName, AnsiLogFormatter formatter,
            LogLevel level, LogCategory category, int indentLevel, string? payload = null, string? sourceId = null)
        {
            var (symbol, label, color) = level switch
            {
                LogLevel.Debug => ("◆", "DEBUG", Brushes.LightGray),
                LogLevel.Information => ("●", "INFO", Brushes.LightSkyBlue),
                LogLevel.Warning => ("▲", "AVERTISSEMENT", Brushes.Orange),
                LogLevel.Error => ("✖", "ERREUR", Brushes.Salmon),
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Niveau de journal invalide.")
            };
            _symbol = symbol;
            _label = label;
            _timestamp = timestamp;
            _message = message;
            _formatter = formatter;
            _category = category;
            _indentLevel = indentLevel;
            LevelColor = color;
            EventName = eventName;
            Payload = payload;
            SourceId = sourceId;
            Level = level;
            RefreshDisplay(showTime: true, dateStamp: false, indent: false);
        }

        public string? EventName { get; }
        public string? Payload { get; }
        public string? SourceId { get; }
        public LogLevel Level { get; }
        public LogCategory Category => _category;
        private System.Windows.Media.Brush LevelColor { get; }
        public TextBlock Display => _display;
        public string Text => _text;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshDisplay(bool showTime, bool dateStamp, bool indent)
        {
            var prefix = $"{_symbol} [{_label}] ";
            if (dateStamp)
                prefix += $"[{_timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}] ";
            if (showTime)
                prefix += $"[{_timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] ";
            if (indent && _indentLevel > 0)
                prefix = new string(' ', _indentLevel * 2) + prefix;

            var display = new TextBlock { Foreground = LevelColor };
            display.Inlines.Add(new Run(prefix));
            var text = new System.Text.StringBuilder(prefix);
            foreach (var segment in _formatter.Parse(_message))
            {
                var run = new Run(segment.Text);
                if (segment.Foreground is { } foreground)
                    run.Foreground = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(foreground.Red, foreground.Green, foreground.Blue));
                if (segment.Background is { } background)
                    run.Background = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(background.Red, background.Green, background.Blue));
                display.Inlines.Add(run);
                text.Append(segment.Text);
            }
            _display = display;
            _text = text.ToString();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }

        public override string ToString() => Text;
    }

    private sealed record SystemMacroChoice(MacroDefinition Macro, string Label);
    private sealed record SystemActionChoice(MacroAction Action, string Label);
    private sealed record ActivationTargetChoice(ActivationTargetKind Kind, string Target, string Value, string Label);
    private sealed record JumpModeChoice(JumpMode Mode, string Label);
    private sealed record IfComparisonChoice(IfComparison Comparison, string Label);
    private sealed record ConditionalJumpModeChoice(ConditionalJumpMode Mode, string Label);
    private sealed record SystemActionResult(
        string Message, ActivationTargetKind Kind, string Target, string Value, bool PreviousState, bool CurrentState);

    private sealed record TriggerEvent(string Name, string? Payload, string? SourceId);

    private sealed class ActionContext(
        MainWindow window, CancellationToken sessionToken, object? initialResult, TriggerEvent? triggerEvent = null,
        int? logIndentLevel = null) : IActionContext
    {
        private readonly object _resultLock = new();
        private readonly AnsiLogFormatter _logFormatter = new();
        private object? _result = initialResult;
        public TriggerEvent? Event => triggerEvent;
        public MainWindow Window => window;
        public CancellationToken CancellationToken => sessionToken;
        public int? LogIndentLevel => logIndentLevel;

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

        public void Log(string message) => Log(message, LogLevel.Information);

        public void Log(string message, LogLevel level)
        {
            if (!Enum.IsDefined(level))
                throw new InvalidOperationException($"Niveau de journal invalide : {level}.");
            window.Dispatcher.BeginInvoke(new Action(() =>
                window.WriteLog(message, null, _logFormatter, level, LogCategory.Action, logIndentLevel ?? 1)));
        }

        public void RaiseEvent(string name, string? payload) => RaiseEventFromSource(name, payload, null);

        public void RaiseEventFromSource(string name, string? payload, string? sourceId) =>
            window.Dispatcher.BeginInvoke(
                new Action(() => _ = window.DispatchEventAsync(name, payload, sessionToken, sourceId)));
    }

    private sealed class PluginHost(MainWindow window, ActionContext context, string? sourceId = null) : IPluginHost
    {
        public IPluginUserInterface UserInterface { get; } = new PluginUserInterface(window);
        public IIdleTimeProvider IdleTime => window._platform.IdleTime;
        public IMonitorService Monitors => window._platform.Monitors;
        public IHidDeviceService HidDevices => window._platform.HidDevices;
        public ISessionState Session => window._platform.Session;
        public object? Result
        {
            get => context.Result;
            set => context.Result = value;
        }

        public IReadOnlyList<PluginActionData> GetConfiguredActions(string pluginId) =>
            window.Macros.SelectMany(macro => macro.Actions).Concat(window.StartupActions)
                .Where(action => string.Equals(action.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                .Select(action => new PluginActionData(action.ActionId, action.Value, action.Target)).ToArray();
        public IReadOnlyList<PluginStartupData> GetPluginStartupConfigurations(string pluginId) =>
            window.PluginStartups.Where(configuration =>
                    string.Equals(configuration.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                .Select(ToPluginStartupData).ToArray();
        public void SetTrayMenuItems(string pluginId, IReadOnlyList<PluginTrayMenuItem> items) =>
            window.SetPluginTrayMenuItems(pluginId, items);
        public IReadOnlyList<PluginMacroDescriptor> GetMacros() =>
            window.Macros.Select(macro => new PluginMacroDescriptor(macro.Id, macro.Name)).ToArray();
        public async Task RunMacroAsync(Guid macroId, CancellationToken cancellationToken)
        {
            await window.Dispatcher.InvokeAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, context.CancellationToken);
                var macro = window.Macros.FirstOrDefault(item => item.Id == macroId)
                    ?? throw new InvalidOperationException($"Macro introuvable : {macroId}.");
                if (!macro.IsEnabled)
                    throw new InvalidOperationException($"La macro « {macro.Name} » est désactivée.");
                await window.ExecuteMacroInSessionAsync(macro, linkedCancellation.Token,
                    logIndentLevel: context.LogIndentLevel ?? 0, actionContextToken: cancellationToken);
            }).Task.Unwrap();
        }
        public void Log(string message) => context.Log(message);
        public void Log(string message, LogLevel level) => context.Log(message, level);
        public void RaiseEvent(string name, string? payload) => context.RaiseEventFromSource(name, payload, sourceId);
    }

    private sealed class PluginUserInterface(Window window) : IPluginUserInterface
    {
        public object? NativeOwner => window;

        public bool? ShowDialog(object dialog)
        {
            if (dialog is not Window wpfDialog)
                throw new ArgumentException("La boîte de dialogue doit être une fenêtre WPF.", nameof(dialog));
            wpfDialog.Owner ??= window;
            return wpfDialog.ShowDialog();
        }

        public void ShowMessage(string message, string title) =>
            MessageBox.Show(window, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public bool Confirm(string message, string title) =>
            MessageBox.Show(window, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes;
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
    private readonly ConfigurationStore _configurationStore;
    private readonly PluginRuntime _pluginRuntime = new();
    private readonly ObservableCollection<EventLogEntry> _logEntries = [];
    private readonly ConditionalWeakTable<ItemsControl, object> _treeConnectorParents = new();
    private ICollectionView? _logView;
    private readonly HashSet<Task> _activeMacroExecutions = [];
    private CancellationTokenSource _sessionCancellation = new();
    private string _cleanConfiguration = string.Empty;
    private MacroDefinition? _selectedMacro;
    private readonly StartupSection _startupSection;
    private readonly ConfigurationRoot _configurationRoot;
    private readonly MacroDefinition _startupMacro;
    private readonly WindowsPlatformServices _platform =
        new(exception => EventGhostDotNet.App.LogError(exception, "Services Windows"));
    private ITrayIconService? _applicationTrayIcon;
    private readonly Dictionary<string, IReadOnlyList<PluginTrayMenuItem>> _pluginTrayMenuItems =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _startMinimizedFromCommandLine;
    private StartupWindowMode _startupWindowMode = StartupWindowMode.Show;
    private string? _powerShellPath;
    private bool _powerShellFallbackWarned;
    private bool _showRequestedFromNotification;
    private bool _applyingDisplaySettings;
    private bool _isExiting;
    private bool _isClosed;
    private readonly EventWaitHandle _exitRequest;
    private readonly RegisteredWaitHandle _exitRequestWait;

    public ObservableCollection<MacroDefinition> Macros { get; } = [];
    public ObservableCollection<MacroGroup> MacroGroups { get; } = [];
    public ObservableCollection<PluginStartupConfiguration> PluginStartups => _startupSection.Configurations;
    public ObservableCollection<MacroAction> StartupActions => _startupSection.Actions;

    public static bool GetIsLastSibling(DependencyObject element) =>
        (bool)element.GetValue(IsLastSiblingProperty);

    public static void SetIsLastSibling(DependencyObject element, bool value) =>
        element.SetValue(IsLastSiblingProperty, value);

    public static bool GetIsTreeRoot(DependencyObject element) =>
        (bool)element.GetValue(IsTreeRootProperty);

    public static void SetIsTreeRoot(DependencyObject element, bool value) =>
        element.SetValue(IsTreeRootProperty, value);

    private void UpdateTreeConnectorLines(ItemsControl parent)
    {
        var generator = parent.ItemContainerGenerator;
        if (!_treeConnectorParents.TryGetValue(parent, out _))
        {
            _treeConnectorParents.Add(parent, new object());
            generator.ItemsChanged += (_, _) => UpdateTreeConnectorLines(parent);
        }

        for (var index = 0; index < parent.Items.Count; index++)
            if (generator.ContainerFromIndex(index) is TreeViewItem item)
            {
                SetIsLastSibling(item, index == parent.Items.Count - 1);
                SetIsTreeRoot(item, parent is System.Windows.Controls.TreeView);
            }
    }

    public MainWindow(bool startMinimizedFromCommandLine = false)
    {
        InitializeComponent();
        _startMinimizedFromCommandLine = startMinimizedFromCommandLine;
        _configurationStore = new ConfigurationStore(_configurationPath, JsonOptions);
        _logView = CollectionViewSource.GetDefaultView(_logEntries);
        _logView.Filter = item => item is EventLogEntry entry && ShouldShowLogEntry(entry);
        LogList.ItemsSource = _logView;
        ConfigurationPathText.Text = _configurationPath;
        _startupSection = new StartupSection();
        _configurationRoot = new ConfigurationRoot(_startupSection);
        _startupMacro = new MacroDefinition(StartupActions) { Name = "Démarrage" };
        ConfigurationTree.ItemsSource = new[] { _configurationRoot };
        _pluginRuntime.Load(Path.Combine(GetApplicationDirectory(), "Plugins"), WriteLog,
            ConfirmUnverifiedPlugins);
        LoadConfiguration();
        if (_startMinimizedFromCommandLine || _startupWindowMode == StartupWindowMode.MinimizeToTray)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
        _cleanConfiguration = SerializeConfiguration();
        InitializeMenuState();
        _exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset,
            $@"Local\EventGhostDotNet.Exit.{Environment.ProcessId}", out var createdNew);
        if (!createdNew)
        {
            _exitRequest.Dispose();
            throw new InvalidOperationException("Le signal de sortie de l'application existe déjà.");
        }
        _exitRequestWait = ThreadPool.RegisterWaitForSingleObject(
            _exitRequest, (_, _) => RaiseSystemTrayEvent(SystemExitEventName),
            null, Timeout.Infinite, false);
    }

    private static void ValidateEntityIds(
        IEnumerable<MacroDefinition> macros, IEnumerable<MacroAction> additionalActions)
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
        foreach (var action in additionalActions)
            if (action.Id == Guid.Empty || !actionIds.Add(action.Id))
                throw new JsonException("Identifiant d'action vide ou dupliqué dans la configuration.");
    }

    private static PluginActionData ToPluginData(MacroAction action) =>
        new(action.ActionId, action.Value, action.Target);

    private static string GetApplicationDirectory()
    {
        var processPath = Environment.ProcessPath;
        // A single-file bundle may extract the entry assembly to a temporary directory.
        if (processPath is not null &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;

        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        return string.IsNullOrEmpty(entryAssemblyPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(entryAssemblyPath) ?? AppContext.BaseDirectory;
    }

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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SelectStartupSection();
        EnsureApplicationTrayIcon();
        await using (await _pluginRuntime.EnterAsync())
        {
            if (_isClosed)
                return;
            try
            {
                await StartConfiguredPluginsAsync();
            }
            catch (OperationCanceledException) when (_isClosed)
            {
                return;
            }
            catch (AggregateException exception)
            {
                if (!_isClosed)
                    MessageBox.Show(this, exception.Message, "Erreur de démarrage de plugin",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (_isClosed)
            return;
        if ((_startMinimizedFromCommandLine || _startupWindowMode == StartupWindowMode.MinimizeToTray) &&
            !_showRequestedFromNotification)
            Hide();
        await ExecuteStartupActionsAsync();
        await DispatchEventAsync(ConfigurationLoadedEventName, null);
        await DispatchEventAsync(ApplicationStartupEventName, null);
    }

    private bool ConfirmUnverifiedPlugins(IReadOnlyList<string> relativePaths)
    {
        var list = string.Join(Environment.NewLine, relativePaths.Select(path => $"• {path}"));
        var message =
            "Ces plugins ne sont pas répertoriés avec une empreinte SHA-256 valide dans " +
            $"Plugins\\plugins.manifest :{Environment.NewLine}{Environment.NewLine}{list}" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "Un plugin s'exécute avec les droits de l'application et peut lire vos fichiers. " +
            "Ne chargez que des plugins de confiance." +
            $"{Environment.NewLine}{Environment.NewLine}Charger ces plugins quand même ?";
        const string title = "Plugins non vérifiés";
        var result = IsLoaded
            ? MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            WriteLog("Plugins non vérifiés refusés par l'utilisateur.", LogLevel.Warning);
            return false;
        }

        WriteLog($"Plugins non vérifiés approuvés pour cette session : {string.Join(", ", relativePaths)}.",
            LogLevel.Warning);
        return true;
    }

    private async void StopPlugins_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await using (await _pluginRuntime.EnterAsync())
            {
                if (_isClosed)
                    return;
                await ResetPluginsAsync();
            }
        }
        catch (AggregateException exception)
        {
            if (!_isClosed)
                MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ReloadPlugins_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await using (await _pluginRuntime.EnterAsync())
            {
                if (_isClosed)
                    return;
                await ResetPluginsAsync();
                await _pluginRuntime.ReloadAsync(Path.Combine(GetApplicationDirectory(), "Plugins"),
                    WriteLog, ConfirmUnverifiedPlugins);
                _pluginTrayMenuItems.Clear();
                RebuildApplicationTrayMenu();
                RefreshPluginStartupDescriptions();
                RefreshPluginDescriptions(Macros);
                RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
                if (_isClosed)
                    return;
                await StartConfiguredPluginsAsync();
            }
            WriteLog("Plugins rechargés.");
        }
        catch (OperationCanceledException) when (_isClosed)
        {
        }
        catch (AggregateException exception)
        {
            if (!_isClosed)
                MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK,
                    MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(() => _ = DispatchEventAsync(SystemHideEventName, null)));
            return;
        }

        if (SerializeConfiguration() == _cleanConfiguration)
            return;

        var result = MessageBox.Show(this,
            "La configuration a été modifiée. Voulez-vous l'enregistrer avant de quitter ?\n\n" +
            "Oui : enregistrer et quitter. Non : quitter sans enregistrer. Annuler : rester dans l'application.",
            "Modifications non enregistrées", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (result == MessageBoxResult.Cancel || (result == MessageBoxResult.Yes && !SaveConfiguration()))
        {
            e.Cancel = true;
            _isExiting = false;
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _exitRequestWait.Unregister(null);
        _exitRequest.Dispose();
        _sessionCancellation.Cancel();
        await using (await _pluginRuntime.EnterAsync())
        {
            _sessionCancellation.Cancel();
            try
            {
                await Task.WhenAll(_activeMacroExecutions.ToArray());
            }
            catch (Exception exception)
            {
                WriteLog($"Erreur d'arrêt des macros : {exception.Message}", LogLevel.Error);
            }
            await _pluginRuntime.DisposeAllAsync(WriteLog);
            DisposeApplicationTrayIcon();
            _sessionCancellation.Dispose();
            _platform.Dispose();
        }
    }

    private void EnsureApplicationTrayIcon()
    {
        if (_applicationTrayIcon is not null)
            return;

        var trayIcon = new WindowsTrayIconService();
        trayIcon.Activated += () => RaiseSystemTrayEvent(SystemShowEventName);
        _applicationTrayIcon = trayIcon;
        trayIcon.Show(Path.Combine(AppContext.BaseDirectory, "Assets", "EventGhostDotNet.ico"), "EventGhost .NET");
        RebuildApplicationTrayMenu();
    }

    private void SetPluginTrayMenuItems(string pluginId, IReadOnlyList<PluginTrayMenuItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        if (snapshot.Any(item => item is null ||
                (!item.IsSeparator && (string.IsNullOrWhiteSpace(item.Label) ||
                                       string.IsNullOrWhiteSpace(item.EventName)))))
            throw new ArgumentException("Les éléments du menu doivent avoir un libellé et un événement.", nameof(items));

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(new Action(() => SetPluginTrayMenuItems(pluginId, snapshot)));
            return;
        }

        if (snapshot.Any(item => !item.IsSeparator))
            _pluginTrayMenuItems[pluginId] = snapshot;
        else
            _pluginTrayMenuItems.Remove(pluginId);
        RebuildApplicationTrayMenu();
    }

    private void RebuildApplicationTrayMenu()
    {
        if (_applicationTrayIcon is null)
            return;

        var items = new List<TrayMenuItem>
        {
            new("Afficher EventGhost", () => RaiseSystemTrayEvent(SystemShowEventName)),
            new("Masquer EventGhost", () => RaiseSystemTrayEvent(SystemHideEventName)),
            TrayMenuItem.Separator
        };

        var customItems = _pluginTrayMenuItems
            .OrderBy(plugin => plugin.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(plugin => plugin.Value)
            .ToList();
        var normalizedItems = new List<PluginTrayMenuItem>();
        foreach (var item in customItems)
        {
            if (item.IsSeparator)
            {
                if (normalizedItems.Count > 0 && !normalizedItems[^1].IsSeparator)
                    normalizedItems.Add(item);
            }
            else
            {
                normalizedItems.Add(item);
            }
        }
        while (normalizedItems.Count > 0 && normalizedItems[^1].IsSeparator)
            normalizedItems.RemoveAt(normalizedItems.Count - 1);

        if (normalizedItems.Count > 0)
        {
            foreach (var item in normalizedItems)
            {
                if (item.IsSeparator)
                {
                    items.Add(TrayMenuItem.Separator);
                    continue;
                }

                items.Add(new TrayMenuItem(item.Label, () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    WriteLog($"System Tray Menu : événement « {item.EventName} » déclenché.");
                    _ = DispatchEventAsync(item.EventName, null);
                }))));
            }
            items.Add(TrayMenuItem.Separator);
        }

        if (!items[^1].IsSeparator)
            items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem("Historique des notifications",
            () => Dispatcher.BeginInvoke(new Action(OpenNotificationHistory))));
        items.Add(new TrayMenuItem("Sortie", () => RaiseSystemTrayEvent(SystemExitEventName)));
        _applicationTrayIcon.SetMenu(items);
    }

    private static void OpenNotificationHistory() =>
        (System.Windows.Application.Current as App)?.ShowNotificationHistory();

    private void RaiseSystemTrayEvent(string eventName) =>
        Dispatcher.BeginInvoke(new Action(() => _ = DispatchEventAsync(eventName, null)));

    internal void ShowFromNotification()
    {
        _showRequestedFromNotification = true;
        RaiseSystemTrayEvent(SystemShowEventName);
    }

    private void DisposeApplicationTrayIcon()
    {
        _applicationTrayIcon?.Dispose();
        _applicationTrayIcon = null;
        _pluginTrayMenuItems.Clear();
    }

    private Task ResetPluginsAsync() => _pluginRuntime.ResetAsync(WriteLog);

    private Task StartConfiguredPluginsAsync() =>
        _pluginRuntime.StartConfiguredAsync(
            PluginStartups,
            (_, _, sourceId) =>
                new PluginHost(this, new ActionContext(this, _sessionCancellation.Token, null), sourceId),
            _sessionCancellation.Token,
            WriteLog);

    private async Task RestartConfiguredPluginsAsync()
    {
        await using (await _pluginRuntime.EnterAsync())
        {
            if (_isClosed)
                return;
            try
            {
                await StartNewSessionAsync();
                if (_isClosed)
                    return;
                await StartConfiguredPluginsAsync();
            }
            catch (OperationCanceledException) when (_isClosed)
            {
                return;
            }
            catch (AggregateException exception)
            {
                WriteLog($"Erreur de redémarrage des plugins : {exception.Message}", LogLevel.Error);
                if (!_isClosed)
                    MessageBox.Show(this, exception.Message, "Erreur de plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        UpdateMenuSelectionState();
    }

    private void ConfigurationTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var target = ConfigurationTree.SelectedItem;
        if (e.CursorLeft >= 0 && e.CursorTop >= 0)
        {
            var source = ConfigurationTree.InputHitTest(Mouse.GetPosition(ConfigurationTree)) as DependencyObject;
            var item = FindTreeItem(source);
            target = item?.DataContext;
            if (item is not null)
                item.IsSelected = true;
        }

        var menu = ConfigurationTree.ContextMenu;
        menu.Items.Clear();

        void Add(string header, RoutedEventHandler handler, bool enabled = true)
        {
            var menuItem = new MenuItem { Header = header, DataContext = target, IsEnabled = enabled };
            menuItem.Click += handler;
            menu.Items.Add(menuItem);
        }

        void AddMoveCommands()
        {
            var (canMoveUp, canMoveDown) = GetMoveAvailability(target);
            Add("Monter", MoveSelectionUp_Click, canMoveUp);
            Add("Descendre", MoveSelectionDown_Click, canMoveDown);
        }

        switch (target)
        {
            case StartupSection:
                Add("Ajouter un plugin", AddPluginStartup_Click);
                Add("Ajouter une action", AddStartupAction_Click);
                break;
            case PluginStartupConfiguration startup:
                Add(startup.IsEnabled ? "Désactiver" : "Activer", TogglePluginStartup_Click);
                Add("Modifier", EditPluginStartupItem_Click, startup.CanConfigure);
                Add("Supprimer", RemovePluginStartupItem_Click);
                break;
            case MacroGroup group:
                Add("Ajouter un sous-groupe", AddMacroGroup_Click);
                Add("Ajouter une macro", AddMacro_Click);
                Add("Renommer", RenameMacroGroupItem_Click);
                Add("Supprimer", RemoveMacroGroupItem_Click,
                    MacroGroups.Count > 1 && group.Macros.Count == 0 && group.Groups.Count == 0);
                Add(group.IsEnabled ? "Désactiver" : "Activer", ToggleSelectedEnabled_Click);
                AddMoveCommands();
                break;
            case MacroDefinition macro:
                Add("Ajouter une action", AddAction_Click);
                Add("Modifier", EditMacroItem_Click);
                Add("Supprimer", RemoveMacroItem_Click);
                Add(macro.IsEnabled ? "Désactiver" : "Activer", (_, _) => macro.IsEnabled = !macro.IsEnabled);
                Add("Exécuter", RunMacro_Click);
                AddMoveCommands();
                Add("Changer de groupe", MoveMacroToGroup_Click, MacroGroups.Count > 1);
                break;
            case MacroEvent macroEvent:
                Add("Ajouter une action", AddAction_Click);
                Add("Modifier", EditEventItem_Click);
                Add("Supprimer", RemoveEventItem_Click);
                Add(macroEvent.IsEnabled ? "Désactiver" : "Activer",
                    (_, _) => macroEvent.IsEnabled = !macroEvent.IsEnabled);
                break;
            case MacroAction action:
                if (!StartupActions.Contains(action))
                    Add("Ajouter une action", AddAction_Click);
                Add("Modifier", EditActionItem_Click, action.Kind != MacroActionKind.Beep);
                Add("Supprimer", RemoveActionItem_Click);
                Add(action.IsEnabled ? "Désactiver" : "Activer", (_, _) => action.IsEnabled = !action.IsEnabled);
                Add("Exécuter", RunMacro_Click);
                AddMoveCommands();
                break;
            case ConfigurationRoot or null:
                Add("Ajouter un groupe", AddMacroGroup_Click);
                break;
        }
    }

    private TreeViewItem? FindTreeItem(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, ConfigurationTree))
        {
            if (source is TreeViewItem item)
                return item;
            source = source switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(source),
                ContentElement content => ContentOperations.GetParent(content) ??
                    (content as FrameworkContentElement)?.Parent,
                _ => LogicalTreeHelper.GetParent(source)
            };
        }

        return null;
    }

    private (bool CanMoveUp, bool CanMoveDown) GetMoveAvailability(object? selectedItem)
    {
        var index = -1;
        var count = 0;
        switch (selectedItem)
        {
            case MacroGroup group:
                var siblings = GetGroupSiblings(group);
                index = siblings.IndexOf(group);
                count = siblings.Count;
                break;
            case MacroDefinition macro:
                var ownerGroup = FindMacroGroup(macro);
                if (ownerGroup is not null)
                {
                    index = ownerGroup.Macros.IndexOf(macro);
                    count = ownerGroup.Macros.Count;
                }
                break;
            case MacroAction action:
                var ownerMacro = Macros.FirstOrDefault(macro => macro.Actions.Contains(action));
                if (ownerMacro is not null)
                {
                    index = ownerMacro.Actions.IndexOf(action);
                    count = ownerMacro.Actions.Count;
                }
                else if (StartupActions.Contains(action))
                {
                    index = StartupActions.IndexOf(action);
                    count = StartupActions.Count;
                }
                break;
        }

        return (index > 0, index >= 0 && index < count - 1);
    }

    private void MoveSelectionUp_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(-1);

    private void MoveSelectionDown_Click(object sender, RoutedEventArgs e) => MoveSelectedItem(1);

    private void MoveSelectedItem(int offset)
    {
        switch (ConfigurationTree.SelectedItem)
        {
            case MacroGroup group when MoveItem(GetGroupSiblings(group), group, offset):
                SynchronizeGroupOrder();
                SynchronizeMacroOrder();
                SelectMacroGroup(group);
                WriteLog($"Groupe « {group.Name} » déplacé à la position {GetGroupSiblings(group).IndexOf(group) + 1}.");
                break;
            case MacroDefinition macro:
                var ownerGroup = FindMacroGroup(macro);
                if (ownerGroup is null || !MoveItem(ownerGroup.Macros, macro, offset))
                    return;
                SynchronizeMacroOrder();
                SelectTreeItem(macro);
                WriteLog($"Macro « {macro.Name} » déplacée à la position {ownerGroup.Macros.IndexOf(macro) + 1} " +
                         $"dans « {ownerGroup.Name} ».");
                break;
            case MacroAction action:
                var ownerMacro = Macros.FirstOrDefault(macro => macro.Actions.Contains(action)) ??
                                 (StartupActions.Contains(action) ? _startupMacro : null);
                if (ownerMacro is null || !MoveItem(ownerMacro.Actions, action, offset))
                    return;
                if (ReferenceEquals(ownerMacro, _startupMacro))
                    SelectStartupActionItem(action);
                else
                    SelectTreeItem(ownerMacro, action);
                WriteLog($"Action déplacée à la position {ownerMacro.Actions.IndexOf(action) + 1} " +
                         $"dans « {ownerMacro.Name} ».");
                break;
        }
    }

    private static bool MoveItem<T>(ObservableCollection<T> items, T item, int offset)
    {
        var currentIndex = items.IndexOf(item);
        var newIndex = currentIndex + offset;
        if (currentIndex < 0 || newIndex < 0 || newIndex >= items.Count)
            return false;
        items.Move(currentIndex, newIndex);
        return true;
    }

    private void TreeItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item)
            return;
        if (ItemsControl.ItemsControlFromItemContainer(item) is ItemsControl parent)
            UpdateTreeConnectorLines(parent);
        switch (item.DataContext)
        {
            case StartupSection section:
                item.IsExpanded = section.IsExpanded;
                break;
            case MacroGroup group:
                item.IsExpanded = group.IsExpanded;
                break;
            case MacroDefinition macro:
                item.IsExpanded = macro.IsExpanded;
                break;
        }
    }

    private void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || !item.IsLoaded || !ReferenceEquals(item, e.OriginalSource))
            return;
        switch (item.DataContext)
        {
            case StartupSection section:
                section.IsExpanded = true;
                break;
            case MacroGroup group:
                group.IsExpanded = true;
                break;
            case MacroDefinition macro:
                macro.IsExpanded = true;
                break;
        }
        SynchronizeHistoryViewBaseline();
    }

    private void TreeItem_Collapsed(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || !item.IsLoaded || !ReferenceEquals(item, e.OriginalSource))
            return;
        switch (item.DataContext)
        {
            case StartupSection section:
                section.IsExpanded = false;
                break;
            case MacroGroup group:
                group.IsExpanded = false;
                break;
            case MacroDefinition macro:
                macro.IsExpanded = false;
                break;
        }
        SynchronizeHistoryViewBaseline();
    }

    private void TreeItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not TreeViewItem item ||
            ShouldIgnoreTreeItemDoubleClick(e.OriginalSource, item))
            return;

        var dataContext = item.DataContext;
        if (dataContext is not (PluginStartupConfiguration or MacroEvent or MacroAction or MacroDefinition or MacroGroup))
            return;

        e.Handled = true;
        item.IsSelected = true;

        switch (dataContext)
        {
            case PluginStartupConfiguration startupConfiguration when startupConfiguration.CanConfigure:
                _ = EditPluginStartupAsync(startupConfiguration);
                break;
            case MacroEvent macroEvent:
                EditEvent(macroEvent);
                break;
            case MacroAction action:
                if (action.Kind == MacroActionKind.Beep)
                    MessageBox.Show(this, "Cette action n'a pas de paramètres à modifier.", "Modification impossible",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    EditAction_Click(sender, e);
                break;
            case MacroDefinition:
                EditMacro_Click(sender, e);
                break;
            case MacroGroup group:
                RenameMacroGroup(group);
                break;
        }
    }

    private static bool ShouldIgnoreTreeItemDoubleClick(object source, TreeViewItem item)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase)
                return true;
            if (current is TreeViewItem sourceItem)
                return !ReferenceEquals(sourceItem, item);
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
            WriteLog($"Impossible de recharger la configuration : {exception.Message}", LogLevel.Error);
            MessageBox.Show(this, $"La configuration actuelle est conservée.\n{exception.Message}",
                "Erreur de rechargement", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await using (await _pluginRuntime.EnterAsync())
        {
            if (_isClosed)
                return;
            try
            {
                await StartNewSessionAsync();
            }
            catch (OperationCanceledException) when (_isClosed)
            {
                return;
            }
            catch (AggregateException exception)
            {
                WriteLog($"Impossible de recharger la configuration : {exception.Message}", LogLevel.Error);
                if (!_isClosed)
                    MessageBox.Show(this, exception.Message, "Erreur de rechargement",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (_isClosed)
                return;
            MacroGroups.Clear();
            Macros.Clear();
            PluginStartups.Clear();
            StartupActions.Clear();
            _startupWindowMode = newConfiguration.StartupWindowMode;
            _powerShellPath = newConfiguration.PowerShellPath;
            foreach (var configuration in newConfiguration.PluginStartups)
                PluginStartups.Add(configuration);
            foreach (var action in newConfiguration.StartupActions)
                StartupActions.Add(action);
            LoadMacroCollections(newConfiguration);
            SetStartupSectionExpanded(newConfiguration.StartupSectionExpanded);
            RefreshPluginStartupDescriptions();
            RefreshPluginDescriptions(Macros);
            RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
            ApplyDisplaySettings(newConfiguration.Display);
            RefreshHistorySubscriptions();
            _undoConfiguration.Clear();
            _redoConfiguration.Clear();
            _lastHistorySnapshot = SerializeConfiguration();
            UpdateMenuSelectionState();
            _cleanConfiguration = SerializeConfiguration();
            SelectStartupSection();

            WriteLog($"Configuration rechargée : {Macros.Count} macro(s).");
            try
            {
                await StartConfiguredPluginsAsync();
            }
            catch (OperationCanceledException) when (_isClosed)
            {
                return;
            }
            catch (AggregateException exception)
            {
                if (!_isClosed)
                    MessageBox.Show(this, exception.Message, "Erreur de démarrage de plugin",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        if (_isClosed)
            return;
        await ExecuteStartupActionsAsync();
        await DispatchEventAsync(ConfigurationLoadedEventName, null);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
        => RequestExit();

    private bool ShowMainWindow()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        return Activate();
    }

    private void RequestExit()
    {
        if (_isExiting || _isClosed)
            return;

        if (SerializeConfiguration() != _cleanConfiguration)
        {
            var wasTopmost = Topmost;
            try
            {
                Topmost = true;
                if (!ShowMainWindow())
                    WriteLog("Windows n'a pas autorisé l'activation de la fenêtre de confirmation.", LogLevel.Warning);
                _isExiting = true;
                Close();
            }
            finally
            {
                if (!_isClosed)
                    Topmost = wasTopmost;
            }
            return;
        }

        _isExiting = true;
        Close();
    }

    private void StartupPreferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Préférences de démarrage",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(18)
        };
        var panel = new StackPanel { Width = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = "Au prochain démarrage, que doit faire EventGhost ?",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var showWindow = new RadioButton
        {
            Content = "Afficher la fenêtre",
            GroupName = "StartupWindowMode",
            IsChecked = _startupWindowMode == StartupWindowMode.Show,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var minimizeToTray = new RadioButton
        {
            Content = "Démarrer minimisée dans la zone de notification",
            GroupName = "StartupWindowMode",
            IsChecked = _startupWindowMode == StartupWindowMode.MinimizeToTray,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(showWindow);
        panel.Children.Add(minimizeToTray);
        panel.Children.Add(new TextBlock
        {
            Text = "Cette préférence est enregistrée avec la configuration.",
            Foreground = EventGhostDotNet.Theme.AppTheme.GetBrush("MutedTextBrush", Brushes.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        });
        var startWithWindows = new CheckBox
        {
            Content = "Démarrer automatiquement avec Windows",
            IsChecked = WindowsStartupRegistration.IsEnabled(),
            Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(startWithWindows);
        panel.Children.Add(new TextBlock
        {
            Text = "L'application est alors lancée minimisée dans la zone de notification " +
                   @"(valeur EventGhostDotNet dans HKCU\Software\Microsoft\Windows\CurrentVersion\Run).",
            Foreground = EventGhostDotNet.Theme.AppTheme.GetBrush("MutedTextBrush", Brushes.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button
        {
            Content = "Enregistrer",
            IsDefault = true
        };
        save.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        var previousMode = _startupWindowMode;
        if (dialog.ShowDialog() != true)
            return;

        _startupWindowMode = minimizeToTray.IsChecked == true
            ? StartupWindowMode.MinimizeToTray
            : StartupWindowMode.Show;
        if (!SaveConfiguration())
        {
            _startupWindowMode = previousMode;
            return;
        }

        try
        {
            WindowsStartupRegistration.SetEnabled(startWithWindows.IsChecked == true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or
                                           System.Security.SecurityException or IOException or
                                           InvalidOperationException)
        {
            MessageBox.Show(this,
                $"Impossible de modifier le démarrage avec Windows : {exception.Message}",
                "Préférences de démarrage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PowerShellPreferences_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Préférences PowerShell",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Padding = new Thickness(18)
        };
        var panel = new StackPanel { Width = 520 };
        panel.Children.Add(new TextBlock
        {
            Text = "PowerShell 7 utilisé par les actions de script",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = EventGhostDotNet.Theme.AppTheme.GetBrush("MutedTextBrush", Brushes.Gray),
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(status);
        var pathBox = new TextBox
        {
            Text = _powerShellPath ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(pathBox);
        var pathButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var browse = new Button { Content = "Parcourir...", Margin = new Thickness(0, 0, 8, 0) };
        var automatic = new Button { Content = "Détection automatique" };
        pathButtons.Children.Add(browse);
        pathButtons.Children.Add(automatic);
        panel.Children.Add(pathButtons);
        panel.Children.Add(new TextBlock
        {
            Text = "Laissez le champ vide pour chercher pwsh.exe dans le PATH et les emplacements " +
                   "d'installation habituels. Si PowerShell 7 reste introuvable, les scripts s'exécutent " +
                   "avec Windows PowerShell 5.1 et un avertissement est journalisé une fois par session.",
            Foreground = EventGhostDotNet.Theme.AppTheme.GetBrush("MutedTextBrush", Brushes.Gray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        void UpdateStatus()
        {
            var configured = PowerShellExecutables.NormalizeConfiguredPath(pathBox.Text);
            var detected = PowerShellExecutables.FindPowerShell7(configured);
            if (detected is not null)
            {
                status.Text = $"PowerShell 7 : {detected}";
                return;
            }

            status.Text = string.IsNullOrWhiteSpace(configured)
                ? "PowerShell 7 introuvable : repli sur Windows PowerShell 5.1."
                : $"Chemin introuvable : {configured}. Détection automatique utilisée à l'exécution.";
        }

        browse.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Sélectionner pwsh.exe",
                Filter = "PowerShell 7 (pwsh.exe)|pwsh.exe|Exécutables (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (picker.ShowDialog(dialog) == true)
            {
                pathBox.Text = picker.FileName;
                UpdateStatus();
            }
        };
        automatic.Click += (_, _) =>
        {
            pathBox.Text = string.Empty;
            UpdateStatus();
        };
        pathBox.TextChanged += (_, _) => UpdateStatus();
        UpdateStatus();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(new Button { Content = "Annuler", IsCancel = true });
        var save = new Button { Content = "Enregistrer", IsDefault = true };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        save.Click += (_, _) =>
        {
            var configured = PowerShellExecutables.NormalizeConfiguredPath(pathBox.Text);
            if (configured is not null && !File.Exists(configured))
            {
                MessageBox.Show(dialog, $"Le fichier indiqué est introuvable :\n{configured}",
                    "Préférences PowerShell", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var previous = _powerShellPath;
            _powerShellPath = configured;
            if (!SaveConfiguration())
            {
                _powerShellPath = previous;
                return;
            }
            dialog.DialogResult = true;
        };

        dialog.ShowDialog();
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var result = await UpdateService.CheckAsync();
        if (result.Error is not null)
        {
            MessageBox.Show(this, result.Error, "Vérification des mises à jour",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            MessageBox.Show(this, $"Vous utilisez la version {result.Current}, qui est à jour.",
                "Vérification des mises à jour", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"La version {result.Latest} est disponible (version actuelle : {result.Current}).\n\n" +
            "Ouvrir la page de téléchargement ?",
            "Mise à jour disponible", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes || result.DownloadUrl is not { Length: > 0 } url)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir la page de téléchargement.\n{exception.Message}",
                "Vérification des mises à jour", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowLogPreview_Click(object sender, RoutedEventArgs e)
    {
        var window = new LogPreviewWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticLog.LogDirectory);
            Process.Start(new ProcessStartInfo(DiagnosticLog.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Impossible d'ouvrir le dossier des journaux.\n\n{exception.Message}",
                "Dossier des journaux", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyLogPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiagnosticLog.LogDirectory);
            StatusText.Text = "Chemin des journaux copié.";
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Impossible de copier le chemin des journaux.\n\n{exception.Message}",
                "Journaux de diagnostic", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearDiagnosticLogs_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Supprimer les journaux de diagnostic (fichiers courants et archives) ?\n\n" +
            DiagnosticLog.LogDirectory,
            "Journaux de diagnostic", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        if (!DiagnosticLog.Clear())
        {
            MessageBox.Show(this,
                "Certains fichiers de journal n'ont pas pu être supprimés. Ils sont peut-être ouverts " +
                "dans un autre programme.",
                "Journaux de diagnostic", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WriteLog("Journaux de diagnostic vidés.");
    }

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
            _startupWindowMode = configuration.StartupWindowMode;
            _powerShellPath = configuration.PowerShellPath;
            foreach (var startup in configuration.PluginStartups)
                PluginStartups.Add(startup);
            foreach (var action in configuration.StartupActions)
                StartupActions.Add(action);
            LoadMacroCollections(configuration);
            SetStartupSectionExpanded(configuration.StartupSectionExpanded);
            RefreshPluginStartupDescriptions();
            RefreshPluginDescriptions(Macros);
            RefreshSystemActionDescriptions(Macros.Append(_startupMacro));
            ApplyDisplaySettings(configuration.Display);
            WriteLog(_configurationStore.Exists
                ? $"Configuration chargée : {Macros.Count} macro(s)."
                : "Configuration initiale créée en mémoire. Ajoutez vos macros puis enregistrez.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _startupWindowMode = StartupWindowMode.Show;
            WriteLog($"Impossible de charger la configuration : {exception.Message}", LogLevel.Error);
            if (MacroGroups.Count == 0)
            {
                var group = new MacroGroup();
                _configurationRoot.Groups.Add(group);
                MacroGroups.Add(group);
            }
            AddMacroToGroup(CreateSampleMacro(), MacroGroups[0]);
        }
    }

    private ConfigurationDocument ReadConfiguration()
    {
        if (!_configurationStore.Exists)
        {
            var initialConfiguration = new ConfigurationDocument { Macros = [CreateSampleMacro()] };
            NormalizeMacroGroups(initialConfiguration);
            return initialConfiguration;
        }

        return ReadConfiguration(_configurationStore.ReadText());
    }

    private ConfigurationDocument ReadConfiguration(string json)
    {
        var configuration = _configurationStore.Deserialize(json);
        configuration.PluginStartups ??= [];
        configuration.StartupActions ??= [];
        configuration.MacroGroups ??= [];
        configuration.Macros ??= [];
        configuration.Display ??= new();
        configuration.PowerShellPath = string.IsNullOrWhiteSpace(configuration.PowerShellPath)
            ? null
            : configuration.PowerShellPath.Trim();

        MigrateLegacyStartupActions(configuration);
        MigrateLegacySystemEvents(configuration);
        NormalizeMacroGroups(configuration);
        foreach (var macro in configuration.Macros)
        {
            if (macro.EventNames.Any(string.IsNullOrWhiteSpace))
                throw new JsonException($"La macro « {macro.Name} » contient un événement vide.");
        }
        foreach (var action in configuration.Macros.SelectMany(macro => macro.Actions)
                     .Concat(configuration.StartupActions))
        {
            if (action.Kind is not (MacroActionKind.Log or MacroActionKind.Delay or MacroActionKind.Beep or
                MacroActionKind.ShowMessage or MacroActionKind.Notify or MacroActionKind.AskYesNo or MacroActionKind.Stop or
                MacroActionKind.OpenUrl or MacroActionKind.PowerShell or MacroActionKind.Plugin or
                MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or MacroActionKind.EnableAction or MacroActionKind.DisableAction or
                MacroActionKind.ActivateItem or MacroActionKind.DeactivateItem or
                MacroActionKind.Jump or MacroActionKind.If or MacroActionKind.IfJump or MacroActionKind.RaiseEvent))
            {
                var match = _pluginRuntime.FindLegacy(action.Kind.ToString());
                if (match is not null)
                {
                    action.Kind = MacroActionKind.Plugin;
                    action.PluginId = match.Value.Plugin.Id;
                    action.ActionId = match.Value.Action.Id;
                }
            }
        }

        EnsurePluginStartupConfigurations(
            configuration.Macros, configuration.PluginStartups, configuration.StartupActions);
        MigratePluginInstances(
            configuration.Macros, configuration.PluginStartups, _pluginRuntime.Plugins, configuration.StartupActions);
        ValidateEntityIds(configuration.Macros, configuration.StartupActions);
        ValidateStartupConfigurationIds(configuration.PluginStartups);
        RefreshSystemActionDescriptions(configuration.Macros.Append(new MacroDefinition(configuration.StartupActions)
        {
            Name = "Démarrage"
        }), configuration.MacroGroups, configuration.StartupActions, configuration.PluginStartups);
        return configuration;
    }

    private static void NormalizeMacroGroups(ConfigurationDocument configuration)
    {
        configuration.MacroGroups ??= [];
        if (configuration.MacroGroups.Any(group => group is null))
            throw new JsonException("La liste des groupes de macros contient un élément nul.");

        foreach (var group in configuration.MacroGroups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
                throw new JsonException("Chaque groupe de macros doit avoir un nom.");
            if (group.Id == Guid.Empty)
                group.Id = Guid.NewGuid();
        }
        if (configuration.MacroGroups.Select(group => group.Id).Distinct().Count() !=
            configuration.MacroGroups.Count)
            throw new JsonException("Les identifiants des groupes de macros doivent être uniques.");

        if (configuration.MacroGroups.Count == 0)
            configuration.MacroGroups.Add(new MacroGroup());

        var groupIds = configuration.MacroGroups.Select(group => group.Id).ToHashSet();
        var groupsById = configuration.MacroGroups.ToDictionary(group => group.Id);
        foreach (var group in configuration.MacroGroups)
        {
            var ancestors = new HashSet<Guid> { group.Id };
            var parentId = group.ParentGroupId;
            while (parentId is { } id)
            {
                if (!groupsById.TryGetValue(id, out var parent))
                    throw new JsonException($"Le groupe parent de « {group.Name} » est introuvable.");
                if (!ancestors.Add(id))
                    throw new JsonException($"La hiérarchie des groupes contient un cycle : « {group.Name} ».");
                parentId = parent.ParentGroupId;
            }
        }
        var defaultGroupId = configuration.MacroGroups[0].Id;
        foreach (var macro in configuration.Macros)
        {
            if (macro.GroupId == Guid.Empty || !groupIds.Contains(macro.GroupId))
                macro.GroupId = defaultGroupId;
        }
    }

    private void LoadMacroCollections(ConfigurationDocument configuration)
    {
        _configurationRoot.Groups.Clear();
        MacroGroups.Clear();
        Macros.Clear();
        foreach (var group in configuration.MacroGroups)
        {
            group.Macros.Clear();
            group.Groups.Clear();
            MacroGroups.Add(group);
        }
        var groupsById = MacroGroups.ToDictionary(group => group.Id);
        foreach (var group in MacroGroups)
        {
            var siblings = group.ParentGroupId is { } parentId
                ? groupsById[parentId].Groups
                : _configurationRoot.Groups;
            siblings.Add(group);
        }
        foreach (var macro in configuration.Macros)
        {
            var group = FindMacroGroup(macro)
                ?? throw new JsonException($"Le groupe de la macro « {macro.Name} » est introuvable.");
            group.Macros.Add(macro);
            Macros.Add(macro);
        }
    }

    private static void MigrateLegacySystemEvents(ConfigurationDocument configuration)
    {
        foreach (var macro in configuration.Macros)
        {
            for (var index = 0; index < macro.EventNames.Count; index++)
                macro.EventNames[index] = MigrateLegacySystemEventName(macro.EventNames[index]);
        }

        foreach (var action in configuration.Macros.SelectMany(macro => macro.Actions)
                     .Concat(configuration.StartupActions))
            if (action.Kind == MacroActionKind.RaiseEvent)
                action.Target = MigrateLegacySystemEventName(action.Target);
    }

    private static string MigrateLegacySystemEventName(string eventName) => eventName switch
    {
        "EGDN.Startup" => "System.Startup",
        "EGDN.ConfigurationLoaded" => "System.ConfigurationLoaded",
        "EGDN.SystemTray.Show" => SystemShowEventName,
        "EGDN.SystemTray.Hide" => SystemHideEventName,
        "EGDN.SystemTray.Exit" => SystemExitEventName,
        _ => eventName
    };

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
        IEnumerable<MacroDefinition> macros,
        ObservableCollection<PluginStartupConfiguration> configurations,
        IEnumerable<MacroAction> additionalActions)
    {
        var usedPluginIds = macros.SelectMany(macro => macro.Actions).Concat(additionalActions)
            .Where(action => action.Kind == MacroActionKind.Plugin)
            .Select(action => action.PluginId)
            .Where(pluginId => !string.IsNullOrWhiteSpace(pluginId))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var pluginId in usedPluginIds)
        {
            if (configurations.Any(configuration =>
                    string.Equals(configuration.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (_pluginRuntime.Find(pluginId) is not IPlugin plugin)
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
        IEnumerable<IPlugin> plugins,
        IEnumerable<MacroAction> additionalActions)
    {
        foreach (var instancePlugin in plugins)
        {
            if (instancePlugin is not IPluginInstanceProvider plugin)
                continue;
            var actions = macros.SelectMany(macro => macro.Actions).Concat(additionalActions)
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
            _cleanConfiguration = _configurationStore.Save(CreateConfigurationDocument());
            WriteLog($"Configuration enregistrée : {Macros.Count} macro(s).");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteLog($"Échec de l'enregistrement : {exception.Message}", LogLevel.Error);
            MessageBox.Show(this, $"La configuration n'a pas pu être enregistrée.\n{exception.Message}",
                "Erreur d'enregistrement", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private ConfigurationDocument CreateConfigurationDocument() =>
        new()
        {
            SchemaVersion = ConfigurationMigrator.CurrentSchemaVersion,
            PluginStartups = PluginStartups,
            StartupActions = StartupActions,
            MacroGroups = MacroGroups,
            Macros = Macros,
            StartupSectionExpanded = _startupSection.IsExpanded,
            StartupWindowMode = _startupWindowMode,
            Display = CaptureDisplaySettings(),
            PowerShellPath = _powerShellPath
        };

    private string SerializeConfiguration() => _configurationStore.Serialize(CreateConfigurationDocument());

    private DisplaySettings CaptureDisplaySettings() => new()
    {
        ShowToolbar = ToolbarVisibilityMenuItem.IsChecked == true,
        AutoExpandOnEvent = AutoExpandOnEventMenuItem.IsChecked == true,
        ShowTime = ShowLogTimeMenuItem.IsChecked == true,
        DateStamp = DateStampLogMenuItem.IsChecked == true,
        Indent = IndentLogMenuItem.IsChecked == true,
        ShowMacros = ShowMacrosInLogMenuItem.IsChecked == true,
        ShowActions = ShowActionsInLogMenuItem.IsChecked == true,
        ShowDebug = LogDebugFilter.IsChecked == true,
        ShowInformation = LogInformationFilter.IsChecked == true,
        ShowWarning = LogWarningFilter.IsChecked == true,
        ShowError = LogErrorFilter.IsChecked == true
    };

    /// <summary>Applique les options d'affichage lues de la configuration, sans les réenregistrer.</summary>
    private void ApplyDisplaySettings(DisplaySettings settings)
    {
        _applyingDisplaySettings = true;
        try
        {
            ToolbarVisibilityMenuItem.IsChecked = settings.ShowToolbar;
            MainToolBarTray.Visibility = settings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
            AutoExpandOnEventMenuItem.IsChecked = settings.AutoExpandOnEvent;
            _autoExpandOnEvent = settings.AutoExpandOnEvent;
            ShowLogTimeMenuItem.IsChecked = settings.ShowTime;
            DateStampLogMenuItem.IsChecked = settings.DateStamp;
            IndentLogMenuItem.IsChecked = settings.Indent;
            ShowMacrosInLogMenuItem.IsChecked = settings.ShowMacros;
            ShowActionsInLogMenuItem.IsChecked = settings.ShowActions;
            ShowDebugLogMenuItem.IsChecked = settings.ShowDebug;
            LogDebugFilter.IsChecked = settings.ShowDebug;
            LogInformationFilter.IsChecked = settings.ShowInformation;
            LogWarningFilter.IsChecked = settings.ShowWarning;
            LogErrorFilter.IsChecked = settings.ShowError;
            foreach (var entry in _logEntries)
                entry.RefreshDisplay(settings.ShowTime, settings.DateStamp, settings.Indent);
            RefreshLogView();
        }
        finally
        {
            _applyingDisplaySettings = false;
        }
    }

    /// <summary>Réenregistre la configuration dès qu'une option d'affichage change.</summary>
    private void PersistDisplaySettings()
    {
        if (_applyingDisplaySettings || SerializeConfiguration() == _cleanConfiguration)
            return;
        try
        {
            _cleanConfiguration = _configurationStore.Save(CreateConfigurationDocument());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteLog($"Impossible d'enregistrer les options d'affichage : {exception.Message}", LogLevel.Warning);
        }
    }

    private static MacroDefinition CreateSampleMacro() =>
        new()
        {
            Name = "Bienvenue",
            EventNames = ["Demo.Start"],
            Actions = [new MacroAction { Kind = MacroActionKind.Log, Value = "EventGhost .NET est prêt." }]
        };

    private void CopyLog_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = LogList.SelectedItems.Count > 0;

    private void CopyLog_Executed(object sender, ExecutedRoutedEventArgs e) => CopySelectedLogEntries();

    private void CopySelectedLogEntries()
    {
        var lines = LogList.SelectedItems.Cast<object>()
            .OrderBy(item => LogList.Items.IndexOf(item))
            .Select(item => item.ToString());
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }
        catch (ExternalException exception)
        {
            WriteLog($"Impossible de copier le journal : {exception.Message}", LogLevel.Error);
            MessageBox.Show(this, exception.Message, "Erreur de presse-papiers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _logEntries.Clear();

    private void LogFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_logView is null)
            return;

        if (ReferenceEquals(sender, LogDebugFilter))
            ShowDebugLogMenuItem.IsChecked = LogDebugFilter.IsChecked == true;
        _logView.Refresh();
        ScrollLogToEnd();
        PersistDisplaySettings();
    }

    private void WriteLog(string message) => WriteLog(message, null);

    private void WriteLog(string message, LogLevel level) => WriteLog(message, null, level: level);

    private void LogList_Loaded(object sender, RoutedEventArgs e) => ScrollLogToEnd();

    private void ScrollLogToEnd()
    {
        if (LogList.Items.Count == 0 || LogList.SelectedItems.Count != 0)
            return;

        FindScrollViewer(LogList)?.ScrollToVerticalOffset(double.PositiveInfinity);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;
            if (FindScrollViewer(child) is ScrollViewer descendant)
                return descendant;
        }

        return null;
    }

    private void WriteLog(
        string message, string? eventName, AnsiLogFormatter? formatter = null, LogLevel level = LogLevel.Information,
        LogCategory category = LogCategory.General, int indentLevel = 0, string? payload = null,
        string? sourceId = null)
    {
        var timestamp = DateTime.Now;
        var entry = new EventLogEntry(timestamp, message, eventName, formatter ?? new AnsiLogFormatter(),
            level, category, indentLevel, payload, sourceId);
        entry.RefreshDisplay(ShowLogTimeMenuItem.IsChecked == true,
            DateStampLogMenuItem.IsChecked == true, IndentLogMenuItem.IsChecked == true);
        _logEntries.Add(entry);
        while (_logEntries.Count > 5000)
            _logEntries.RemoveAt(0);
        DiagnosticLog.Write(timestamp, level, category.ToString(), indentLevel, message);
        ScrollLogToEnd();
    }

    private static int NextLogIndentLevel(int level) => Math.Min(level + 1, MaximumLogIndentLevel);

    private string? PromptForText(string title, string prompt, string initialValue)
    {
        var value = Interaction.InputBox(prompt, title, initialValue);
        return string.IsNullOrEmpty(value) ? null : value;
    }

}