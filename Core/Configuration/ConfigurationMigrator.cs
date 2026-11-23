using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EventGhostDotNet.Core.Configuration;

/// <summary>
/// Met la configuration au format courant avant sa désérialisation :
/// racine sous forme de tableau (ancien format), noms d'événements <c>EGDN.*</c>
/// et numéro de version de schéma.
/// </summary>
public static class ConfigurationMigrator
{
    public const int CurrentSchemaVersion = 2;
    public const string SchemaVersionProperty = "SchemaVersion";

    private static readonly JsonSerializerOptions SerializationOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Normalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Le fichier de configuration est vide.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException exception)
        {
            throw new JsonException($"Le fichier de configuration n'est pas un JSON valide : {exception.Message}", exception);
        }

        var document = root switch
        {
            JsonObject configuration => configuration,
            JsonArray macros => new JsonObject { ["Macros"] = macros.DeepClone() },
            null => throw new JsonException("Le fichier de configuration est vide."),
            _ => throw new JsonException("Le fichier de configuration doit contenir un tableau ou un objet.")
        };

        MigrateLegacySystemEventNames(document);
        document[SchemaVersionProperty] = CurrentSchemaVersion;
        return document.ToJsonString(SerializationOptions);
    }

    public static string MapLegacySystemEventName(string eventName) => eventName switch
    {
        "EGDN.Startup" => "System.Startup",
        "EGDN.ConfigurationLoaded" => "System.ConfigurationLoaded",
        "EGDN.SystemTray.Show" => "System.Show",
        "EGDN.SystemTray.Hide" => "System.Hide",
        "EGDN.SystemTray.Exit" => "System.Exit",
        _ => eventName
    };

    private static void MigrateLegacySystemEventNames(JsonObject document)
    {
        foreach (var macro in EnumerateObjects(document["Macros"]))
        {
            if (macro["EventNames"] is not JsonArray eventNames)
                continue;
            for (var index = 0; index < eventNames.Count; index++)
                if (eventNames[index] is JsonValue value &&
                    value.TryGetValue<string>(out var eventName))
                    eventNames[index] = MapLegacySystemEventName(eventName);

            foreach (var action in EnumerateObjects(macro["Actions"]))
                MigrateRaiseEventTarget(action);
        }

        foreach (var action in EnumerateObjects(document["StartupActions"]))
            MigrateRaiseEventTarget(action);
    }

    private static void MigrateRaiseEventTarget(JsonObject action)
    {
        if (action["Kind"] is not JsonValue kindValue ||
            !kindValue.TryGetValue<string>(out var kind) ||
            !string.Equals(kind, "RaiseEvent", StringComparison.Ordinal) ||
            action["Target"] is not JsonValue targetValue ||
            !targetValue.TryGetValue<string>(out var target))
            return;
        action["Target"] = MapLegacySystemEventName(target);
    }

    private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? node) =>
        node is JsonArray array
            ? array.OfType<JsonObject>()
            : Enumerable.Empty<JsonObject>();
}
