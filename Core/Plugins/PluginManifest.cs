namespace EventGhostDotNet.Core.Plugins;

/// <summary>
/// Détermine quelles DLL du dossier <c>Plugins</c> sont des plugins.
/// Un fichier <c>plugins.manifest</c> (une DLL par ligne, chemins relatifs) prend le pas ;
/// sinon la convention <c>Plugins/&lt;Nom&gt;/&lt;Nom&gt;.dll</c> est utilisée, ce qui évite de
/// tenter de charger les dépendances d'un plugin.
/// </summary>
public static class PluginManifest
{
    public const string FileName = "plugins.manifest";

    public static IReadOnlyList<string> ResolveAssemblyPaths(string pluginsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
        if (!Directory.Exists(pluginsDirectory))
            return [];

        var manifestPath = Path.Combine(pluginsDirectory, FileName);
        return File.Exists(manifestPath)
            ? Parse(File.ReadAllText(manifestPath), pluginsDirectory)
            : ScanConventionalLayout(pluginsDirectory);
    }

    public static IReadOnlyList<string> Parse(string manifest, string pluginsDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        var root = Path.GetFullPath(pluginsDirectory);
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = manifest.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (!line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Ligne {index + 1} du manifeste : seul un chemin vers une DLL est autorisé.");
            var path = Path.GetFullPath(Path.Combine(root, line.Replace('\\', Path.DirectorySeparatorChar)));
            if (!IsInside(root, path))
                throw new InvalidDataException(
                    $"Ligne {index + 1} du manifeste : le chemin sort du dossier des plugins.");
            if (seen.Add(path))
                paths.Add(path);
        }
        return paths;
    }

    public static IReadOnlyList<string> ScanConventionalLayout(string pluginsDirectory)
    {
        var root = Path.GetFullPath(pluginsDirectory);
        var paths = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(root)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(directory, Path.GetFileName(directory) + ".dll");
            if (File.Exists(candidate))
                paths.Add(candidate);
        }
        foreach (var assembly in Directory.EnumerateFiles(root, "*.dll")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            paths.Add(assembly);
        return paths;
    }

    private static bool IsInside(string root, string path) =>
        path.StartsWith(
            Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
