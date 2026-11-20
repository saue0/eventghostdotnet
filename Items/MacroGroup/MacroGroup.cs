using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace EventGhostDotNet;

public sealed class MacroGroup : ConfigurationContainer
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

    public MacroGroup() => Macros = new ConfigurationItemCollection<MacroDefinition>(this);

    public override IEnumerable<ConfigurationItem> GetChildren() => Macros;
}
