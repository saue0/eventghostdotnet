using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using EventGhostDotNet.PluginContracts;

namespace EventGhostDotNet.Core.Plugins;

public sealed class PluginLoader
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
    private readonly List<PluginLoadContext> _contexts = [];

    public IEnumerable<IPlugin> Plugins => _plugins.Values;

    /// <summary>
    /// Charge les plugins listés dans <c>plugins.manifest</c>. Les DLL dont l'empreinte SHA-256
    /// ne correspond pas sont refusées ; celles dont l'empreinte est absente sont soumises à
    /// <paramref name="approveUnverified"/>, qui reçoit leurs chemins relatifs.
    /// </summary>
    public void Load(string directory, Action<string, LogLevel> log,
        Func<IReadOnlyList<string>, bool>? approveUnverified = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (!Directory.Exists(directory))
        {
            log($"Dossier des plugins introuvable : {directory}", LogLevel.Warning);
            return;
        }

        IReadOnlyList<PluginManifestEntry> entries;
        try
        {
            entries = PluginManifest.ResolveEntries(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log($"Impossible de lire la liste des plugins : {exception.Message}", LogLevel.Error);
            return;
        }

        var root = Path.GetFullPath(directory);
        var approved = new List<string>();
        var unverified = new List<string>();
        foreach (var entry in entries)
        {
            var path = entry.Path;
            if (IsContractAssembly(path))
                continue;
            if (!File.Exists(path))
            {
                log($"Assembly de plugin introuvable : {path}", LogLevel.Warning);
                continue;
            }

            if (entry.Sha256 is null)
            {
                unverified.Add(path);
                continue;
            }

            string actual;
            try
            {
                actual = PluginIntegrity.ComputeSha256(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log($"Impossible de vérifier le plugin {path} : {exception.Message}", LogLevel.Error);
                continue;
            }

            if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                log($"Empreinte SHA-256 invalide pour {path} : le plugin n'est pas chargé.", LogLevel.Error);
                continue;
            }
            approved.Add(path);
        }

        if (unverified.Count > 0)
        {
            var relativePaths = unverified
                .Select(path => Path.GetRelativePath(root, path))
                .ToArray();
            if (approveUnverified is not null && approveUnverified(relativePaths))
            {
                approved.AddRange(unverified);
            }
            else
            {
                foreach (var path in unverified)
                    log($"Plugin non vérifié et non approuvé, ignoré : {path}", LogLevel.Warning);
            }
        }

        foreach (var path in approved)
            LoadAssembly(path, log);
    }

    private void LoadAssembly(string path, Action<string, LogLevel> log)
    {
        var context = new PluginLoadContext(path);
        _contexts.Add(context);
        try
        {
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

    /// <summary>
    /// Décharge tous les contextes de chargement. Les plugins doivent avoir été libérés
    /// (et le dictionnaire vidé) avant l'appel ; les contextes encore vivants sont signalés.
    /// </summary>
    public void UnloadAll(Action<string, LogLevel> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _plugins.Clear();
        if (_contexts.Count == 0)
            return;

        var references = UnloadContexts(_contexts);

        for (var attempt = 0; attempt < 5 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        var alive = references.Count(reference => reference.IsAlive);
        if (alive > 0)
        {
            log($"{alive} contexte(s) de chargement de plugin n'ont pas pu être déchargés. " +
                "Un plugin conserve probablement des références statiques ; un redémarrage de " +
                "l'application est nécessaire pour les libérer.", LogLevel.Warning);
        }
        else
        {
            log("Contextes de chargement des plugins déchargés.", LogLevel.Information);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] UnloadContexts(List<PluginLoadContext> contexts)
    {
        var references = new WeakReference[contexts.Count];
        for (var index = 0; index < contexts.Count; index++)
        {
            references[index] = new WeakReference(contexts[index]);
            contexts[index].Unload();
        }
        contexts.Clear();
        return references;
    }

    private static bool IsContractAssembly(string path) =>
        string.Equals(Path.GetFileName(path), typeof(IPlugin).Assembly.GetName().Name + ".dll",
            StringComparison.OrdinalIgnoreCase);

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
