using EventGhostDotNet.Core.Macros;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class MacroTemplateTests
{
    [Fact]
    public void Jeton_du_resultat_est_remplace()
    {
        Assert.Equal("Vous avez répondu Oui", MacroTemplate.Expand(
            "Vous avez répondu {{$eg.result}}", path => path == "result" ? "Oui" : null));
    }

    [Fact]
    public void Plusieurs_jetons_et_espaces_sont_acceptes()
    {
        Assert.Equal("A = 1, B = 2", MacroTemplate.Expand(
            "A = {{ $eg.first }}, B = {{$EG.second}}",
            path => path switch { "first" => "1", "second" => "2", _ => null }));
    }

    [Fact]
    public void Valeur_absente_donne_une_chaine_vide()
    {
        Assert.Equal("Vous avez répondu ", MacroTemplate.Expand(
            "Vous avez répondu {{$eg.result}}", _ => string.Empty));
    }

    [Fact]
    public void Jeton_inconnu_est_conserve()
    {
        Assert.Equal("{{$eg.inconnu}}", MacroTemplate.Expand("{{$eg.inconnu}}", _ => null));
    }

    [Fact]
    public void Texte_sans_jeton_est_inchange()
    {
        Assert.Equal("Texte simple", MacroTemplate.Expand("Texte simple", _ => "valeur"));
    }

    [Fact]
    public void Chemin_de_variable_est_transmis_tel_quel()
    {
        Assert.Equal("valeur", MacroTemplate.Expand(
            "{{$eg.variable.Ma Variable}}", path => path == "variable.Ma Variable" ? "valeur" : null));
    }
}
