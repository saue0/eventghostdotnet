using System.Collections.ObjectModel;
using EventGhostDotNet.Core.Configuration;

namespace EventGhostDotNet;

internal enum StartupWindowMode
{
    Show,
    MinimizeToTray
}

/// <summary>
/// Forme sérialisée de la configuration. <see cref="SchemaVersion"/> permet aux
/// migrations de reconnaître le format d'un fichier au lieu de le deviner.
/// </summary>
internal sealed class ConfigurationDocument
{
    public int SchemaVersion { get; set; } = ConfigurationMigrator.CurrentSchemaVersion;
    public ObservableCollection<PluginStartupConfiguration> PluginStartups { get; set; } = [];
    public ObservableCollection<MacroAction> StartupActions { get; set; } = [];
    public ObservableCollection<MacroGroup> MacroGroups { get; set; } = [];
    public ObservableCollection<MacroDefinition> Macros { get; set; } = [];
    public bool StartupSectionExpanded { get; set; } = true;
    public StartupWindowMode StartupWindowMode { get; set; } = StartupWindowMode.Show;
}
