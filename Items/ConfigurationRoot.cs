using System.Collections.ObjectModel;
using System.Windows.Data;

namespace EventGhostDotNet;

public sealed class ConfigurationRoot : ConfigurationContainer
{
    public string Name => "Configuration";
    public ObservableCollection<MacroGroup> Groups { get; }
    public CompositeCollection Children { get; }

    internal ConfigurationRoot(StartupSection startup)
    {
        Groups = new ConfigurationItemCollection<MacroGroup>(this);
        startup.Parent = this;
        Children = [startup, new CollectionContainer { Collection = Groups }];
    }

    public override IEnumerable<ConfigurationItem> GetChildren() =>
        Children.OfType<StartupSection>().Cast<ConfigurationItem>().Concat(Groups);
}
