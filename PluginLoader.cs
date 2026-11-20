using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet;

internal sealed class PluginLoader
{
    private sealed class PluginLoadContext(string path) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver = new(path);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(IPlugin).Assembly.GetName().Name)
                return typeof(IPlugin).Assembly;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    private readonly Dictionary<string, IPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);
    public IEnumerable<IPlugin> Plugins => _plugins.Values;

    public void Load(string directory, Action<string, LogLevel> log)
    {
        if (!Directory.Exists(directory))
        {
            log($"Dossier des plugins introuvable : {directory}", LogLevel.Warning);
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(path), typeof(IPlugin).Assembly.GetName().Name + ".dll",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var context = new PluginLoadContext(path);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
                foreach (var type in assembly.GetExportedTypes()
                             .Where(type => typeof(IPlugin).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface))
                    LoadPlugin(type, log);
            }
            catch (Exception exception)
            {
                log($"Impossible de charger le plugin {path} : {exception.Message}", LogLevel.Error);
            }
        }
    }

    private void LoadPlugin(Type type, Action<string, LogLevel> log)
    {
        IPlugin? plugin = null;
        try
        {
            plugin = (IPlugin?)Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Impossible de créer le plugin {type.FullName}.");
            var id = plugin.Id;
            if (string.IsNullOrWhiteSpace(id) || _plugins.ContainsKey(id))
                throw new InvalidOperationException($"Identifiant de plugin vide ou déjà utilisé : {id}");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plugin.Actions.Any(action => string.IsNullOrWhiteSpace(action.Id) || !ids.Add(action.Id)))
                throw new InvalidOperationException($"Identifiant d'action vide ou dupliqué dans {id}.");
            var name = plugin.Name;
            _plugins.Add(id, plugin);
            plugin = null;
            log($"Plugin chargé : {name} ({id}).", LogLevel.Information);
        }
        catch (Exception exception)
        {
            log($"Impossible de charger le plugin {type.FullName} : {exception.Message}", LogLevel.Error);
            if (plugin is not null)
            {
                try
                {
                    plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception disposeException)
                {
                    log($"Impossible de libérer le plugin {type.FullName} : {disposeException.Message}", LogLevel.Error);
                }
            }
        }
    }

    public IPlugin? Find(string id) => _plugins.GetValueOrDefault(id);

    public (IPlugin Plugin, PluginActionDescriptor Action)? FindLegacy(string actionId)
    {
        var matches = Plugins.SelectMany(plugin => plugin.Actions
            .Where(action => string.Equals(action.Id, actionId, StringComparison.Ordinal))
            .Select(action => (Plugin: plugin, Action: action))).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
