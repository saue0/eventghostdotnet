using System.Windows;

namespace EventGhostDotNet.PluginContracts;

public sealed record PluginActionData(string ActionId, string Value, string Target);

public sealed record PluginActionDescriptor(string Id, string Name);

public interface IPluginHost
{
    Window Owner { get; }
    IReadOnlyList<PluginActionData> GetConfiguredActions(string pluginId);
    void Log(string message);
    void RaiseEvent(string name, string? payload);
}

public interface IPlugin : IAsyncDisposable
{
    string Id { get; }
    string Name { get; }
    IReadOnlyList<PluginActionDescriptor> Actions { get; }
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
