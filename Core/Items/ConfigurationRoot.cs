using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace EventGhostDotNet;

public sealed class ConfigurationRoot : ConfigurationContainer
{
    public string Name => "Configuration";
    public ObservableCollection<MacroGroup> Groups { get; }
    [JsonIgnore]
    public StartupSection Startup { get; }

    internal ConfigurationRoot(StartupSection startup)
    {
        Groups = new ConfigurationItemCollection<MacroGroup>(this);
        Startup = startup;
        startup.Parent = this;
    }

    public override IEnumerable<ConfigurationItem> GetChildren() => Groups;
}
