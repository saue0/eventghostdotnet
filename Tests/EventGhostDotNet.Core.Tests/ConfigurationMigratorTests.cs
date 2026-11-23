using System.Text.Json;
using System.Text.Json.Nodes;
using EventGhostDotNet.Core.Configuration;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class ConfigurationMigratorTests
{
    [Fact]
    public void Ancien_format_tableau_devient_un_document()
    {
        const string legacy = """
            [ { "Name": "Macro", "EventNames": ["Demo.Start"], "Actions": [] } ]
            """;

        var document = JsonNode.Parse(ConfigurationMigrator.Normalize(legacy))!.AsObject();

        Assert.Equal(ConfigurationMigrator.CurrentSchemaVersion,
            document[ConfigurationMigrator.SchemaVersionProperty]!.GetValue<int>());
        Assert.Equal("Macro", document["Macros"]!.AsArray()[0]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public void Numeros_de_version_est_estampille_sur_un_document_existant()
    {
        var document = JsonNode.Parse(ConfigurationMigrator.Normalize("""{ "Macros": [] }"""))!.AsObject();

        Assert.Equal(ConfigurationMigrator.CurrentSchemaVersion,
            document[ConfigurationMigrator.SchemaVersionProperty]!.GetValue<int>());
    }

    [Fact]
    public void Evenements_EGDN_sont_migres_dans_les_declencheurs()
    {
        const string legacy = """
            {
              "Macros": [
                {
                  "EventNames": ["EGDN.SystemTray.Show", "Perso.Evenement"],
                  "Actions": [
                    { "Kind": "RaiseEvent", "Target": "EGDN.SystemTray.Exit" },
                    { "Kind": "Log", "Target": "EGDN.Startup" }
                  ]
                }
              ]
            }
            """;

        var document = JsonNode.Parse(ConfigurationMigrator.Normalize(legacy))!.AsObject();
        var macro = document["Macros"]!.AsArray()[0]!;

        Assert.Equal(["System.Show", "Perso.Evenement"],
            macro["EventNames"]!.AsArray().Select(node => node!.GetValue<string>()));
        var actions = macro["Actions"]!.AsArray();
        Assert.Equal("System.Exit", actions[0]!["Target"]!.GetValue<string>());
        Assert.Equal("EGDN.Startup", actions[1]!["Target"]!.GetValue<string>());
    }

    [Fact]
    public void Actions_de_demarrage_sont_migrees()
    {
        const string legacy = """
            { "StartupActions": [ { "Kind": "RaiseEvent", "Target": "EGDN.ConfigurationLoaded" } ] }
            """;

        var document = JsonNode.Parse(ConfigurationMigrator.Normalize(legacy))!.AsObject();

        Assert.Equal("System.ConfigurationLoaded",
            document["StartupActions"]!.AsArray()[0]!["Target"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ pas du json")]
    [InlineData("42")]
    public void Entree_invalide_leve_une_JsonException(string json)
    {
        Assert.Throws<JsonException>(() => ConfigurationMigrator.Normalize(json));
    }

    [Fact]
    public void Nom_d_evenement_inconnu_est_conserve()
    {
        Assert.Equal("System.Startup", ConfigurationMigrator.MapLegacySystemEventName("EGDN.Startup"));
        Assert.Equal("Perso.Startup", ConfigurationMigrator.MapLegacySystemEventName("Perso.Startup"));
    }
}
