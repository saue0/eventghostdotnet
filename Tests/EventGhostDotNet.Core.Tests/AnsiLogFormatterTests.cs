using EventGhostDotNet.Core.Logging;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class AnsiLogFormatterTests
{
    [Fact]
    public void Texte_sans_sequence_produit_un_seul_segment()
    {
        var runs = new AnsiLogFormatter().Parse("Bonjour");

        var run = Assert.Single(runs);
        Assert.Equal("Bonjour", run.Text);
        Assert.Null(run.Foreground);
        Assert.Null(run.Background);
    }

    [Fact]
    public void Couleur_standard_et_remise_a_zero()
    {
        var runs = new AnsiLogFormatter().Parse("\u001b[31mrouge\u001b[0mnormal");

        Assert.Equal(2, runs.Count);
        Assert.Equal("rouge", runs[0].Text);
        Assert.Equal(AnsiColor.FromRgb(197, 15, 31), runs[0].Foreground);
        Assert.Equal("normal", runs[1].Text);
        Assert.Null(runs[1].Foreground);
    }

    [Fact]
    public void Couleur_256_utilise_la_palette_etendue()
    {
        var runs = new AnsiLogFormatter().Parse("\u001b[38;5;196mX");

        Assert.Equal(AnsiColor.FromRgb(255, 0, 0), Assert.Single(runs).Foreground);
    }

    [Fact]
    public void Couleur_RGB_est_convertie()
    {
        var runs = new AnsiLogFormatter().Parse("\u001b[38;2;10;20;30mX");

        Assert.Equal(AnsiColor.FromRgb(10, 20, 30), Assert.Single(runs).Foreground);
    }

    [Fact]
    public void L_etat_des_couleurs_persiste_entre_les_appels()
    {
        var formatter = new AnsiLogFormatter();
        formatter.Parse("\u001b[32mvert");

        var runs = formatter.Parse("suite");

        Assert.Equal(AnsiColor.FromRgb(19, 161, 14), Assert.Single(runs).Foreground);
    }

    [Fact]
    public void Sequence_invalide_est_ignoree()
    {
        var runs = new AnsiLogFormatter().Parse("\u001b[38;2;10mfichu");

        Assert.Equal("fichu", Assert.Single(runs).Text);
        Assert.Null(runs[0].Foreground);
    }
}
