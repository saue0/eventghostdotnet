using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Data;

namespace EventGhostDotNet;

public sealed class StartupSection(
    ObservableCollection<PluginStartupConfiguration> configurations,
    ObservableCollection<MacroAction> actions)
{
    public string Name => "Démarrage";
    public bool IsExpanded { get; set; } = true;
    public CompositeCollection Children { get; } =
    [
        new CollectionContainer { Collection = configurations },
        new CollectionContainer { Collection = actions }
    ];
}

public sealed class PluginStartupConfiguration : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private bool _canConfigure;

    public string PluginId { get; set; } = string.Empty;
    public string ConfigurationId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public string Value { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
                return;
            _displayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    [JsonIgnore]
    public bool CanConfigure
    {
        get => _canConfigure;
        set
        {
            if (_canConfigure == value)
                return;
            _canConfigure = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConfigure)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class MacroGroup : INotifyPropertyChanged
{
    private string _name = "Général";
    private bool _isExpanded = true;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
                return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    [JsonIgnore]
    public ObservableCollection<MacroDefinition> Macros { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class MacroDefinition : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isEnabled = true;
    private bool _isExpanded = true;
    private ObservableCollection<string> _eventNames = [];
    private ObservableCollection<string> _disabledEventNames = [];
    private readonly ObservableCollection<MacroEvent> _eventItems = [];
    private ObservableCollection<MacroAction> _actions = [];
    private readonly CollectionContainer _eventContainer = new();
    private readonly CollectionContainer _actionContainer = new();

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public MacroDefinition()
    {
        _eventNames.CollectionChanged += EventNames_CollectionChanged;
        EventItems = new ReadOnlyObservableCollection<MacroEvent>(_eventItems);
        _eventContainer.Collection = _eventItems;
        _actionContainer.Collection = _actions;
        TreeChildren = [_eventContainer, _actionContainer];
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
                return;
            _name = value;
            OnPropertyChanged();
        }
    }

    [JsonRequired]
    public ObservableCollection<string> EventNames
    {
        get => _eventNames;
        set
        {
            if (value is null)
                throw new JsonException("La liste des événements ne peut pas être nulle.");
            if (ReferenceEquals(_eventNames, value))
                return;
            _eventNames.CollectionChanged -= EventNames_CollectionChanged;
            _eventNames = value;
            _eventNames.CollectionChanged += EventNames_CollectionChanged;
            RebuildEventItems();
            OnPropertyChanged();
            OnPropertyChanged(nameof(EventNamesDisplay));
        }
    }

    public ObservableCollection<string> DisabledEventNames
    {
        get => _disabledEventNames;
        set => _disabledEventNames = value ?? throw new JsonException("La liste des événements désactivés ne peut pas être nulle.");
    }

    [JsonIgnore]
    public string EventNamesDisplay => string.Join(", ", EventNames);

    [JsonIgnore]
    public ReadOnlyObservableCollection<MacroEvent> EventItems { get; }

    public bool MatchesEvent(string eventName) =>
        EventItems.Any(item => item.IsEnabled &&
            string.Equals(item.Name, eventName, StringComparison.OrdinalIgnoreCase));

    public void SetEventNames(IReadOnlyList<string> eventNames)
    {
        EventNames = new ObservableCollection<string>(eventNames);
    }

    public ObservableCollection<MacroAction> Actions
    {
        get => _actions;
        set
        {
            _actions = value ?? throw new JsonException("La liste des actions ne peut pas être nulle.");
            _actionContainer.Collection = _actions;
        }
    }

    [JsonIgnore]
    public CompositeCollection TreeChildren { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void EventNames_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                    _eventItems.Insert(e.NewStartingIndex + i, new MacroEvent(this, (string)e.NewItems[i]!));
                break;
            case NotifyCollectionChangedAction.Remove:
                for (var i = 0; i < e.OldItems!.Count; i++)
                    _eventItems.RemoveAt(e.OldStartingIndex);
                break;
            case NotifyCollectionChangedAction.Replace:
                for (var i = 0; i < e.NewItems!.Count; i++)
                    _eventItems[e.NewStartingIndex + i].SetName((string)e.NewItems[i]!);
                break;
            case NotifyCollectionChangedAction.Move:
                _eventItems.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Reset:
                RebuildEventItems();
                break;
        }
        PruneDisabledEvents();
        OnPropertyChanged(nameof(EventNamesDisplay));
    }

    private void RebuildEventItems()
    {
        _eventItems.Clear();
        foreach (var eventName in _eventNames)
            _eventItems.Add(new MacroEvent(this, eventName));
        PruneDisabledEvents();
    }

    private void PruneDisabledEvents()
    {
        foreach (var name in _disabledEventNames
                     .Where(name => !_eventNames.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray())
            _disabledEventNames.Remove(name);
    }

    internal void RemoveDisabledEvent(string name)
    {
        foreach (var disabledName in _disabledEventNames
                     .Where(disabledName => string.Equals(disabledName, name, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            _disabledEventNames.Remove(disabledName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class MacroEvent : INotifyPropertyChanged
{
    internal MacroEvent(MacroDefinition macro, string name)
    {
        Macro = macro;
        Name = name;
    }

    public MacroDefinition Macro { get; }
    public string Name { get; private set; }
    public bool IsEnabled
    {
        get => !Macro.DisabledEventNames.Contains(Name, StringComparer.OrdinalIgnoreCase);
        set
        {
            if (IsEnabled == value)
                return;
            if (value)
                Macro.RemoveDisabledEvent(Name);
            else if (!Macro.DisabledEventNames.Contains(Name, StringComparer.OrdinalIgnoreCase))
                Macro.DisabledEventNames.Add(Name);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetName(string name)
    {
        if (Name == name)
            return;
        var wasEnabled = IsEnabled;
        if (!wasEnabled)
        {
            Macro.RemoveDisabledEvent(Name);
            if (!Macro.DisabledEventNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                Macro.DisabledEventNames.Add(name);
        }
        Name = name;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }
}

public sealed class MacroAction : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private string? _systemDisplayName;
    private string? _pluginDisplayName;

    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public MacroActionKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string PluginId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; set; }

    [JsonIgnore]
    public string? PluginDisplayName
    {
        get => _pluginDisplayName;
        set
        {
            if (_pluginDisplayName == value)
                return;
            _pluginDisplayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    [JsonIgnore]
    public string? SystemDisplayName
    {
        get => _systemDisplayName;
        set
        {
            if (_systemDisplayName == value)
                return;
            _systemDisplayName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonIgnore]
    public string DisplayName => Kind switch
    {
        MacroActionKind.Log => $"Journaliser : {Value}",
        MacroActionKind.Delay => $"Attendre : {Value} ms",
        MacroActionKind.Beep => "Émettre un signal sonore",
        MacroActionKind.RaiseEvent => $"Déclencher l'événement : {Target}",
        MacroActionKind.ShowMessage => $"Afficher un message : {Value}",
        MacroActionKind.OpenUrl => $"Ouvrir l'URL : {Value}",
        MacroActionKind.PowerShell => $"Script PowerShell : {Value.Split(['\r', '\n'], 2)[0]}",
        MacroActionKind.DisplayInputSource => $"Entrée écran : {Target} → {Value}",
        MacroActionKind.LogitechStart => $"Logitech — démarrer l'écoute : {Value}",
        MacroActionKind.LogitechChangeHost => $"Logitech — changer d'hôte : {Value}",
        MacroActionKind.LogitechSendRaw => $"Logitech — envoyer HID : {Value}",
        MacroActionKind.Plugin => PluginDisplayName ?? $"{PluginId} — {ActionId} : {Value}",
        MacroActionKind.EnableMacro or MacroActionKind.DisableMacro or
        MacroActionKind.EnableAction or MacroActionKind.DisableAction =>
            SystemDisplayName ?? "Action système : cible indisponible",
        MacroActionKind.Jump or MacroActionKind.If =>
            SystemDisplayName ?? "Branchement système : configuration indisponible",
        _ => Kind.ToString()
    };

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JumpMode? JumpMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IfComparison? IfComparison { get; set; }
}

public enum MacroActionKind
{
    Log,
    Delay,
    Beep,
    ShowMessage,
    OpenUrl,
    PowerShell,
    DisplayInputSource,
    LogitechStart,
    LogitechChangeHost,
    LogitechSendRaw,
    Plugin,
    EnableMacro,
    DisableMacro,
    EnableAction,
    DisableAction,
    Jump,
    If,
    RaiseEvent
}

public enum JumpMode
{
    CallAndStop,
    CallAndResume,
    GotoAction
}

public enum IfComparison
{
    Equals,
    Contains,
    NotEquals
}
