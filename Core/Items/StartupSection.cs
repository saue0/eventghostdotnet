using System.Collections.ObjectModel;

namespace EventGhostDotNet;

public sealed class StartupSection : ConfigurationContainer
{
    public string Name => "Démarrage";
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<PluginStartupConfiguration> Configurations { get; }
    public ObservableCollection<MacroAction> Actions { get; }

    public StartupSection()
    {
        Configurations = new ConfigurationItemCollection<PluginStartupConfiguration>(this);
        Actions = new ConfigurationItemCollection<MacroAction>(this);
    }

    public override IEnumerable<ConfigurationItem> GetChildren() =>
        Configurations.Cast<ConfigurationItem>().Concat(Actions);
}
