using EventGhostDotNet.Core.Plugins;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class PluginIntegrityTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "egdn-tests", Guid.NewGuid().ToString("N"));

    public PluginIntegrityTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Calcule_une_empreinte_sha256_et_la_compare_sans_casse()
    {
        var path = Path.Combine(_directory, "Plugin.dll");
        File.WriteAllText(path, "contenu du plugin");

        var hash = PluginIntegrity.ComputeSha256(path);

        Assert.Equal(PluginManifest.Sha256HexLength, hash.Length);
        Assert.True(PluginIntegrity.Matches(path, hash));
        Assert.True(PluginIntegrity.Matches(path, hash.ToUpperInvariant()));
    }

    [Fact]
    public void Une_empreinte_differente_est_rejetee()
    {
        var path = Path.Combine(_directory, "Plugin.dll");
        File.WriteAllText(path, "contenu du plugin");

        Assert.False(PluginIntegrity.Matches(path, new string('0', PluginManifest.Sha256HexLength)));
    }
}
