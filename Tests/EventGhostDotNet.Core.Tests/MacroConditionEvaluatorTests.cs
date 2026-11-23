using System.Text.Json;
using EventGhostDotNet.Core.Macros;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class MacroConditionEvaluatorTests
{
    [Fact]
    public void Resultat_absent_est_faux()
    {
        Assert.False(MacroConditionEvaluator.Evaluate(null, IfOperator.Equals, "x"));
        Assert.False(MacroConditionEvaluator.Evaluate(null, IfOperator.NotEquals, "x"));
    }

    [Theory]
    [InlineData("Oui", IfOperator.Equals, "oui", true)]
    [InlineData("Oui", IfOperator.Equals, "non", false)]
    [InlineData("Bonjour le monde", IfOperator.Contains, "LE MONDE", true)]
    [InlineData("Bonjour", IfOperator.Contains, "soir", false)]
    [InlineData("Oui", IfOperator.NotEquals, "non", true)]
    [InlineData("Oui", IfOperator.NotEquals, "oui", false)]
    public void Comparaisons_sont_insensibles_a_la_casse(string result, IfOperator comparison, string expected, bool value)
    {
        Assert.Equal(value, MacroConditionEvaluator.Evaluate(result, comparison, expected));
    }

    [Fact]
    public void Operateur_inconnu_leve_une_exception()
    {
        Assert.Throws<InvalidOperationException>(
            () => MacroConditionEvaluator.Evaluate("x", (IfOperator)42, "x"));
    }

    [Fact]
    public void Mise_en_forme_des_resultats()
    {
        Assert.Null(MacroValueFormatter.Format(null));
        Assert.Equal("texte", MacroValueFormatter.Format("texte"));
        Assert.Equal("42", MacroValueFormatter.Format(42));
        Assert.Equal("1.5", MacroValueFormatter.Format(1.5));
        Assert.Equal("abc", MacroValueFormatter.Format(JsonDocument.Parse("\"abc\"").RootElement));
        Assert.Equal("12", MacroValueFormatter.Format(JsonDocument.Parse("12").RootElement));
    }
}
