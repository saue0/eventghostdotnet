using System.Collections.ObjectModel;
using System.Windows.Data;

namespace EventGhostDotNet;

public sealed class StartupSection : ConfigurationContainer
{
    public string Name => "Démarrage";
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<PluginStartupConfiguration> Configurations { get; }
    public ObservableCollection<MacroAction> Actions { get; }
    public CompositeCollection Children { get; }

    public StartupSection()
    {
        Configurations = new ConfigurationItemCollection<PluginStartupConfiguration>(this);
        Actions = new ConfigurationItemCollection<MacroAction>(this);
        Children =
        [
            new CollectionContainer { Collection = Configurations },
            new CollectionContainer { Collection = Actions }
        ];
    }

    public override IEnumerable<ConfigurationItem> GetChildren() =>
        Configurations.Cast<ConfigurationItem>().Concat(Actions);
}
