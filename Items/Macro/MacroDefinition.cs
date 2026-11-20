using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Data;

namespace EventGhostDotNet;

public sealed class MacroDefinition : ConfigurationContainer
{
    private string _name = string.Empty;
    private bool _isEnabled = true;
    private bool _isExpanded = true;
    private ObservableCollection<string> _eventNames = [];
    private ObservableCollection<string> _disabledEventNames = [];
    private readonly ObservableCollection<MacroEvent> _eventItems;
    private ObservableCollection<MacroAction> _actions;
    private readonly bool _isExecutionContext;
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
            NotifyChanged();
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
            NotifyChanged();
        }
    }

    public MacroDefinition() : this(null) { }

    internal MacroDefinition(ObservableCollection<MacroAction>? executionActions)
    {
        _isExecutionContext = executionActions is not null;
        _eventItems = new ConfigurationItemCollection<MacroEvent>(this);
        _actions = executionActions ?? new ConfigurationItemCollection<MacroAction>(this);
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
            NotifyChanged();
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
            NotifyChanged();
            NotifyChanged(nameof(EventNamesDisplay));
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
            if (value is null)
                throw new JsonException("La liste des actions ne peut pas être nulle.");
            if (ReferenceEquals(_actions, value))
                return;
            if (_isExecutionContext)
            {
                _actions = value;
                _actionContainer.Collection = _actions;
                return;
            }

            var actions = value.ToArray();
            if (actions.Any(action => action is null ||
                    (action.Parent is not null && !ReferenceEquals(action.Parent, this))) ||
                actions.Distinct(ReferenceEqualityComparer.Instance).Count() != actions.Length)
                throw new InvalidOperationException("Une action ne peut appartenir qu'à un seul conteneur.");
            _actions.Clear();
            foreach (var action in actions)
                _actions.Add(action);
        }
    }

    [JsonIgnore]
    public CompositeCollection TreeChildren { get; }

    // The startup execution context shares actions owned by StartupSection; it is not a tree node.
    public override IEnumerable<ConfigurationItem> GetChildren() =>
        _isExecutionContext ? Enumerable.Empty<ConfigurationItem>() :
        _eventItems.Cast<ConfigurationItem>().Concat(_actions);

    protected override void OnParentChanged()
    {
        if (Parent is MacroGroup group)
            GroupId = group.Id;
    }

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
        NotifyChanged(nameof(EventNamesDisplay));
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

}
