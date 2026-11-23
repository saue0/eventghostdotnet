using EventGhostDotNet.Core.Storage;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "egdn-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Ecriture_cree_le_dossier_et_le_fichier()
    {
        var path = Path.Combine(_directory, "sous-dossier", "configuration.json");

        AtomicFile.WriteAllText(path, "contenu");

        Assert.Equal("contenu", File.ReadAllText(path));
        Assert.False(File.Exists(path + AtomicFile.TemporarySuffix));
        Assert.False(File.Exists(path + AtomicFile.BackupSuffix));
    }

    [Fact]
    public void Ecriture_suivante_conserve_l_ancienne_version_en_bak()
    {
        var path = Path.Combine(_directory, "configuration.json");

        AtomicFile.WriteAllText(path, "version 1");
        AtomicFile.WriteAllText(path, "version 2");

        Assert.Equal("version 2", File.ReadAllText(path));
        Assert.Equal("version 1", File.ReadAllText(path + AtomicFile.BackupSuffix));
        Assert.False(File.Exists(path + AtomicFile.TemporarySuffix));
    }

    [Fact]
    public void Ecriture_remplace_un_fichier_existant()
    {
        var path = Path.Combine(_directory, "configuration.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "ancien");

        AtomicFile.WriteAllText(path, "nouveau");

        Assert.Equal("nouveau", File.ReadAllText(path));
    }
}
