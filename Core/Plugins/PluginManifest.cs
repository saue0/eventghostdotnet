namespace EventGhostDotNet.Core.Plugins;

/// <summary>
/// Une entrée de <c>plugins.manifest</c> : chemin de la DLL et empreinte SHA-256 facultative.
/// Une empreinte absente signale un plugin non vérifié, qui doit être approuvé par l'utilisateur.
/// </summary>
public sealed record PluginManifestEntry(string Path, string? Sha256);

/// <summary>
/// Détermine quelles DLL du dossier <c>Plugins</c> sont des plugins.
/// Un fichier <c>plugins.manifest</c> (une DLL par ligne, chemin relatif, empreinte SHA-256
/// facultative après un <c>|</c>) prend le pas ; sinon la convention
/// <c>Plugins/&lt;Nom&gt;/&lt;Nom&gt;.dll</c> est utilisée, ce qui évite de tenter de charger
/// les dépendances d'un plugin.
/// </summary>
public static class PluginManifest
{
    public const string FileName = "plugins.manifest";
    public const int Sha256HexLength = 64;

    public static IReadOnlyList<string> ResolveAssemblyPaths(string pluginsDirectory) =>
        ResolveEntries(pluginsDirectory).Select(entry => entry.Path).ToArray();

    public static IReadOnlyList<PluginManifestEntry> ResolveEntries(string pluginsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);
        if (!Directory.Exists(pluginsDirectory))
            return [];

        var manifestPath = Path.Combine(pluginsDirectory, FileName);
        return File.Exists(manifestPath)
            ? ParseEntries(File.ReadAllText(manifestPath), pluginsDirectory)
            : ScanConventionalLayout(pluginsDirectory)
                .Select(path => new PluginManifestEntry(path, null))
                .ToArray();
    }

    public static IReadOnlyList<string> Parse(string manifest, string pluginsDirectory) =>
        ParseEntries(manifest, pluginsDirectory).Select(entry => entry.Path).ToArray();

    public static IReadOnlyList<PluginManifestEntry> ParseEntries(string manifest, string pluginsDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        var root = Path.GetFullPath(pluginsDirectory);
        var entries = new List<PluginManifestEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = manifest.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var parts = line.Split('|');
            if (parts.Length > 2)
                throw new InvalidDataException(
                    $"Ligne {index + 1} du manifeste : format attendu « chemin.dll|empreinte SHA-256 ».");
            var relativePath = parts[0].Trim();
            if (!relativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Ligne {index + 1} du manifeste : seul un chemin vers une DLL est autorisé.");
            string? sha256 = null;
            if (parts.Length == 2)
            {
                sha256 = parts[1].Trim().ToLowerInvariant();
                if (sha256.Length != Sha256HexLength || !sha256.All(char.IsAsciiHexDigit))
                    throw new InvalidDataException(
                        $"Ligne {index + 1} du manifeste : l'empreinte SHA-256 doit contenir " +
                        $"{Sha256HexLength} caractères hexadécimaux.");
            }

            var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar)));
            if (!IsInside(root, path))
                throw new InvalidDataException(
                    $"Ligne {index + 1} du manifeste : le chemin sort du dossier des plugins.");
            if (seen.Add(path))
                entries.Add(new PluginManifestEntry(path, sha256));
        }
        return entries;
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
