using EventGhostDotNet.Core.Plugins;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet;

/// <summary>
/// Chargement et cycle de vie des plugins, extraits de la fenêtre principale :
/// découverte, verrou de cycle de vie, démarrage, arrêt et libération.
/// </summary>
internal sealed class PluginRuntime
{
    private readonly PluginLoader _loader = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IEnumerable<IPlugin> Plugins => _loader.Plugins;

    public void Load(string directory, Action<string, LogLevel> log) => _loader.Load(directory, log);

    public IPlugin? Find(string id) => _loader.Find(id);

    public (IPlugin Plugin, PluginActionDescriptor Action)? FindLegacy(string actionId) =>
        _loader.FindLegacy(actionId);

    /// <summary>Verrouille le cycle de vie des plugins jusqu'à la libération du jeton retourné.</summary>
    public async ValueTask<IAsyncDisposable> EnterAsync()
    {
        await _gate.WaitAsync();
        return new Releaser(_gate);
    }

    public async Task StartConfiguredAsync(
        IEnumerable<PluginStartupConfiguration> configurations,
        Func<PluginStartupConfiguration, PluginStartupData, string, IPluginHost> hostFactory,
        CancellationToken cancellationToken,
        Action<string, LogLevel> log)
    {
        var failures = new List<Exception>();
        foreach (var configuration in configurations.ToArray())
        {
            if (!configuration.IsEnabled)
                continue;
            var plugin = _loader.Find(configuration.PluginId);
            if (plugin is null)
            {
                var error = new InvalidOperationException($"Plugin introuvable : {configuration.PluginId}");
                log($"Démarrage impossible : {error.Message}", LogLevel.Error);
                failures.Add(error);
                continue;
            }

            var data = ToPluginStartupData(configuration);
            try
            {
                var sourceId = plugin.DescribeStartup(data);
                await plugin.StartupAsync(data, hostFactory(configuration, data, sourceId), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                log($"Erreur de démarrage du plugin {plugin.Id} : {exception.Message}", LogLevel.Error);
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("Certains plugins n'ont pas pu démarrer.", failures);
        log("Démarrage des plugins terminé.", LogLevel.Information);
    }

    public async Task ResetAsync(Action<string, LogLevel> log)
    {
        var failures = new List<Exception>();
        foreach (var plugin in _loader.Plugins)
        {
            try
            {
                await plugin.ResetAsync();
            }
            catch (Exception exception)
            {
                log($"Erreur d'arrêt du plugin {plugin.Id} : {exception.Message}", LogLevel.Error);
                failures.Add(exception);
            }
        }
        if (failures.Count > 0)
            throw new AggregateException("Certains plugins n'ont pas pu être arrêtés.", failures);
        log("Plugins arrêtés.", LogLevel.Information);
    }

    public async Task DisposeAllAsync(Action<string, LogLevel> log)
    {
        foreach (var plugin in _loader.Plugins)
        {
            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception exception)
            {
                log($"Erreur de fermeture du plugin {plugin.Id} : {exception.Message}", LogLevel.Error);
            }
        }
    }

    private static PluginStartupData ToPluginStartupData(PluginStartupConfiguration configuration) =>
        new(configuration.ConfigurationId, configuration.InstanceId, configuration.Value, configuration.Target);

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
