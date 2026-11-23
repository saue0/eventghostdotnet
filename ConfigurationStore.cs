using System.IO;
using System.Text.Json;
using EventGhostDotNet.Core.Configuration;
using EventGhostDotNet.Core.Storage;

namespace EventGhostDotNet;

/// <summary>
/// Lecture et écriture de la configuration : normalisation du schéma historique,
/// sérialisation et écriture atomique avec sauvegarde <c>.bak</c>.
/// </summary>
internal sealed class ConfigurationStore(string path, JsonSerializerOptions options)
{
    public string Path { get; } = path;

    public string BackupPath => Path + AtomicFile.BackupSuffix;

    public bool Exists => File.Exists(Path);

    public string ReadText() => File.ReadAllText(Path);

    public ConfigurationDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<ConfigurationDocument>(ConfigurationMigrator.Normalize(json), options)
        ?? throw new JsonException("Le fichier ne contient pas de configuration.");

    public string Serialize(ConfigurationDocument document) =>
        JsonSerializer.Serialize(document, options);

    /// <summary>Écrit la configuration et retourne le JSON exact enregistré.</summary>
    public string Save(ConfigurationDocument document)
    {
        var json = Serialize(document);
        AtomicFile.WriteAllText(Path, json);
        return json;
    }
}
