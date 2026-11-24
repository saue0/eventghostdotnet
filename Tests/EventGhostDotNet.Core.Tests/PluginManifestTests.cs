using EventGhostDotNet.Core.Plugins;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class PluginManifestTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "egdn-tests", Guid.NewGuid().ToString("N"));

    public PluginManifestTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Manifeste_resout_les_chemins_relatifs_et_ignore_les_commentaires()
    {
        var manifest = """
            # plugins intégrés
            Display\Display.dll

            A/A.dll
            A/A.dll
            """;

        var paths = PluginManifest.Parse(manifest, _directory);

        Assert.Equal(2, paths.Count);
        Assert.Equal(Path.Combine(_directory, "Display", "Display.dll"), paths[0]);
        Assert.Equal(Path.Combine(_directory, "A", "A.dll"), paths[1]);
    }

    [Fact]
    public void Manifeste_lit_les_empreintes_sha256()
    {
        var hash = new string('a', PluginManifest.Sha256HexLength);
        var entries = PluginManifest.ParseEntries($"Display\\Display.dll|{hash}", _directory);

        var entry = Assert.Single(entries);
        Assert.Equal(Path.Combine(_directory, "Display", "Display.dll"), entry.Path);
        Assert.Equal(hash, entry.Sha256);
    }

    [Fact]
    public void Sans_empreinte_l_entree_est_non_verifiee()
    {
        var entries = PluginManifest.ParseEntries("A/A.dll", _directory);

        Assert.Null(Assert.Single(entries).Sha256);
    }

    [Theory]
    [InlineData("A/A.dll|")]
    [InlineData("A/A.dll|abc")]
    [InlineData("A/A.dll|zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Manifeste_refuse_une_empreinte_invalide(string manifest)
    {
        Assert.Throws<InvalidDataException>(() => PluginManifest.ParseEntries(manifest, _directory));
    }

    [Fact]
    public void La_convention_produit_des_entrees_non_verifiees()
    {
        CreateFile("A/A.dll");

        var entries = PluginManifest.ResolveEntries(_directory);

        Assert.Null(Assert.Single(entries).Sha256);
    }

    [Fact]
    public void Manifeste_refuse_un_chemin_hors_du_dossier()
    {
        Assert.Throws<InvalidDataException>(
            () => PluginManifest.Parse(@"..\..\evil.dll", _directory));
    }

    [Fact]
    public void Manifeste_refuse_un_fichier_non_dll()
    {
        Assert.Throws<InvalidDataException>(
            () => PluginManifest.Parse("plugin.exe", _directory));
    }

    [Fact]
    public void Sans_manifeste_la_convention_ignore_les_dependances()
    {
        CreateFile("A/A.dll");
        CreateFile("A/Newtonsoft.Json.dll");
        CreateFile("B/Other.dll");
        CreateFile("Top.dll");

        var paths = PluginManifest.ResolveAssemblyPaths(_directory);

        Assert.Equal(
            [Path.Combine(_directory, "A", "A.dll"), Path.Combine(_directory, "Top.dll")],
            paths);
    }

    [Fact]
    public void Le_manifeste_prime_sur_la_convention()
    {
        CreateFile("A/A.dll");
        CreateFile("B/B.dll");
        File.WriteAllText(Path.Combine(_directory, PluginManifest.FileName), "B/B.dll");

        var paths = PluginManifest.ResolveAssemblyPaths(_directory);

        Assert.Equal([Path.Combine(_directory, "B", "B.dll")], paths);
    }

    [Fact]
    public void Dossier_absent_retourne_une_liste_vide()
    {
        Assert.Empty(PluginManifest.ResolveAssemblyPaths(Path.Combine(_directory, "absent")));
    }

    private void CreateFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }
}
