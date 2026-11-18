using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventGhostDotNet;

public sealed class MacroDefinition : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isEnabled = true;
    private ObservableCollection<string> _eventNames = [];

    public Guid Id { get; set; } = Guid.NewGuid();

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

    public MacroDefinition()
    {
        _eventNames.CollectionChanged += EventNames_CollectionChanged;
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(EventNamesDisplay));
        }
    }

    [JsonIgnore]
    public string EventNamesDisplay => string.Join(", ", EventNames);

    public bool MatchesEvent(string eventName) =>
        EventNames.Any(name => string.Equals(name, eventName, StringComparison.OrdinalIgnoreCase));

    public void SetEventNames(IReadOnlyList<string> eventNames)
    {
        if (eventNames.Count == 0)
            throw new ArgumentException("Au moins un événement est requis.", nameof(eventNames));
        EventNames = new ObservableCollection<string>(eventNames);
    }

    public ObservableCollection<MacroAction> Actions { get; set; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void EventNames_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(EventNamesDisplay));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class MacroAction : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private string? _systemDisplayName;

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
    public string? PluginDisplayName { get; set; }

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
        _ => Kind.ToString()
    };
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
    DisableAction
}
