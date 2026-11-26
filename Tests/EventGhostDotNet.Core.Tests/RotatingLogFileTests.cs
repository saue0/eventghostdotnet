using EventGhostDotNet.Core.Diagnostics;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class RotatingLogFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "egdn-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Ecriture_cree_le_dossier_et_ajoute_les_lignes()
    {
        var path = Path.Combine(_directory, "logs", "eventghost.log");
        var log = new RotatingLogFile(path);

        log.Write("première");
        log.Write("deuxième");

        Assert.Equal($"première{Environment.NewLine}deuxième{Environment.NewLine}", File.ReadAllText(path));
    }

    [Fact]
    public void Depassement_de_la_taille_renomme_le_fichier_en_archive()
    {
        var path = Path.Combine(_directory, "errors.log");
        var log = new RotatingLogFile(path, maximumBytes: 10, maximumArchives: 2);

        log.Write("123456789");
        log.Write("second");

        Assert.Equal($"second{Environment.NewLine}", File.ReadAllText(path));
        Assert.Equal($"123456789{Environment.NewLine}", File.ReadAllText(log.GetArchivePath(1)));
        Assert.False(File.Exists(log.GetArchivePath(2)));
    }

    [Fact]
    public void Les_archives_sont_decalees_et_les_plus_anciennes_supprimees()
    {
        var path = Path.Combine(_directory, "errors.log");
        var log = new RotatingLogFile(path, maximumBytes: 10, maximumArchives: 2);

        log.Write("123456789");
        log.Write("second");
        log.Write("third");
        log.Write("fourth");

        Assert.Equal($"fourth{Environment.NewLine}", File.ReadAllText(path));
        Assert.Equal($"third{Environment.NewLine}", File.ReadAllText(log.GetArchivePath(1)));
        Assert.Equal($"second{Environment.NewLine}", File.ReadAllText(log.GetArchivePath(2)));
        Assert.False(File.Exists($"{path}.3"));
    }

    [Fact]
    public void ReadTail_renvoie_les_dernières_lignes()
    {
        var path = Path.Combine(_directory, "eventghost.log");
        var log = new RotatingLogFile(path);
        for (var index = 1; index <= 5; index++)
            log.Write($"ligne {index}");

        Assert.Equal(["ligne 4", "ligne 5"], log.ReadTail(2));
        Assert.Equal(5, log.ReadTail(10).Count);
    }

    [Fact]
    public void ReadTail_sans_fichier_renvoie_vide()
    {
        var log = new RotatingLogFile(Path.Combine(_directory, "absent.log"));

        Assert.Empty(log.ReadTail(10));
    }

    [Fact]
    public void ReadTail_refuse_un_nombre_de_lignes_non_positif()
    {
        var log = new RotatingLogFile(Path.Combine(_directory, "eventghost.log"));

        Assert.Throws<ArgumentOutOfRangeException>(() => log.ReadTail(0));
    }

    [Fact]
    public void Clear_supprime_le_fichier_et_les_archives_puis_la_rotation_reprend()
    {
        var path = Path.Combine(_directory, "errors.log");
        var log = new RotatingLogFile(path, maximumBytes: 10, maximumArchives: 2);
        log.Write("123456789");
        log.Write("second");

        log.Clear();

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(log.GetArchivePath(1)));
        Assert.False(File.Exists(log.GetArchivePath(2)));

        log.Write("après");
        Assert.Equal($"après{Environment.NewLine}", File.ReadAllText(path));
    }

    [Fact]
    public void GetArchivePath_refuse_un_index_hors_limites()
    {
        var log = new RotatingLogFile(Path.Combine(_directory, "errors.log"), maximumArchives: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => log.GetArchivePath(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => log.GetArchivePath(3));
    }
}
