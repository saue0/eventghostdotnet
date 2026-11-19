using System.Windows;

namespace EventGhostDotNet.PluginContracts;

public sealed record PluginActionData(string ActionId, string Value, string Target);

public sealed record PluginActionDescriptor(string Id, string Name);

public sealed record PluginStartupData(string ConfigurationId, string InstanceId, string Value, string Target);

public sealed record PluginTrayMenuItem(string Label, string EventName, bool IsSeparator = false);

public interface IActionContext
{
    object? Result
    {
        get => null;
        set => throw new NotSupportedException("Ce contexte ne prend pas en charge les résultats d'action.");
    }

    void Log(string message);
    void RaiseEvent(string name, string? payload);
}

public interface IPluginHost : IActionContext
{
    Window Owner { get; }
    IReadOnlyList<PluginActionData> GetConfiguredActions(string pluginId);
    IReadOnlyList<PluginStartupData> GetPluginStartupConfigurations(string pluginId) => [];
    void SetTrayMenuItems(string pluginId, IReadOnlyList<PluginTrayMenuItem> items) { }
}

public interface IPlugin : IAsyncDisposable
{
    string Id { get; }
    string Name { get; }
    IReadOnlyList<PluginActionDescriptor> Actions { get; }
    bool HasStartupConfiguration => false;
    PluginStartupData CreateDefaultStartupConfiguration() =>
        new("Startup", Guid.NewGuid().ToString(), string.Empty, string.Empty);
    PluginStartupData? ConfigureStartup(PluginStartupData? existing, IPluginHost host) =>
        existing ?? CreateDefaultStartupConfiguration();
    string DescribeStartup(PluginStartupData configuration) => $"{Name} — démarrage";
    Task StartupAsync(PluginStartupData configuration, IPluginHost host, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    PluginActionData? ConfigureAction(string actionId, PluginActionData? existing, IPluginHost host);
    string DescribeAction(PluginActionData action);
    Task ExecuteActionAsync(PluginActionData action, IPluginHost host, CancellationToken cancellationToken);
    Task ActionChangedAsync(PluginActionData previous, PluginActionData updated) => Task.CompletedTask;
    Task ResetAsync();
}

public interface IPluginInstanceProvider
{
    bool IsInstanceConfiguration(string actionId);
    bool RequiresInstance(string actionId);
    string GetInstanceName(PluginActionData configuration);
    bool MatchesInstance(PluginActionData action, PluginActionData configuration);
    PluginActionData BindInstance(PluginActionData action, PluginActionData configuration);
    PluginActionData? ConfigureInstanceAction(
        string actionId, PluginActionData? existing, PluginActionData configuration, IPluginHost host);
}
