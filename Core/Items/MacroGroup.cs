using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace EventGhostDotNet;

public sealed class MacroGroup : ConfigurationContainer
{
    private string _name = "Général";
    private bool _isEnabled = true;
    private bool _isExpanded = true;

    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ParentGroupId { get; set; }
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

    [JsonIgnore]
    public ObservableCollection<MacroDefinition> Macros { get; }
    [JsonIgnore]
    public ObservableCollection<MacroGroup> Groups { get; }
    public MacroGroup()
    {
        Macros = new ConfigurationItemCollection<MacroDefinition>(this);
        Groups = new ConfigurationItemCollection<MacroGroup>(this);
    }

    public override IEnumerable<ConfigurationItem> GetChildren() =>
        Groups.Cast<ConfigurationItem>().Concat(Macros);

    protected override void OnParentChanged()
    {
        if (Parent is MacroGroup group)
            ParentGroupId = group.Id;
        else if (Parent is ConfigurationRoot)
            ParentGroupId = null;
    }
}
