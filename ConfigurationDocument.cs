using System.Collections.ObjectModel;
using EventGhostDotNet.Core.Configuration;

namespace EventGhostDotNet;

internal enum StartupWindowMode
{
    Show,
    MinimizeToTray
}

/// <summary>
/// Options d'affichage et de filtrage conservées avec la configuration.
/// </summary>
internal sealed class DisplaySettings
{
    public bool ShowToolbar { get; set; } = true;
    public bool AutoExpandOnEvent { get; set; } = true;
    public bool ShowTime { get; set; } = true;
    public bool DateStamp { get; set; }
    public bool Indent { get; set; }
    public bool ShowMacros { get; set; } = true;
    public bool ShowActions { get; set; } = true;
    public bool ShowDebug { get; set; } = true;
    public bool ShowInformation { get; set; } = true;
    public bool ShowWarning { get; set; } = true;
    public bool ShowError { get; set; } = true;
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
    public DisplaySettings Display { get; set; } = new();
}
