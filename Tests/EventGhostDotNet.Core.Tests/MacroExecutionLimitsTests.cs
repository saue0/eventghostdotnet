using EventGhostDotNet.Core.Macros;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public sealed class MacroExecutionLimitsTests
{
    [Fact]
    public void Profondeur_d_appel_limitee()
    {
        MacroExecutionLimits.EnsureCallDepth(MacroExecutionLimits.MaximumCallDepth - 1);

        var exception = Assert.Throws<InvalidOperationException>(
            () => MacroExecutionLimits.EnsureCallDepth(MacroExecutionLimits.MaximumCallDepth));
        Assert.Contains("Limite".Length > 0 ? "profondeur maximale" : string.Empty, exception.Message);
    }

    [Fact]
    public void Nombre_d_executions_d_actions_limite()
    {
        Assert.True(MacroExecutionLimits.CanExecuteAction(MacroExecutionLimits.MaximumActionExecutionsPerMacro));
        Assert.False(MacroExecutionLimits.CanExecuteAction(MacroExecutionLimits.MaximumActionExecutionsPerMacro + 1));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("250", 250)]
    [InlineData("600000", 600000)]
    public void Delai_valide(string value, int expected)
    {
        Assert.Equal(expected, MacroExecutionLimits.ParseDelay(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("600001")]
    [InlineData("abc")]
    public void Delai_invalide(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => MacroExecutionLimits.ParseDelay(value));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/page", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("pas une url", false)]
    [InlineData(null, false)]
    public void Seules_les_url_http_sont_autorisees(string? value, bool expected)
    {
        Assert.Equal(expected, MacroExecutionLimits.IsAllowedUrl(value, out _));
    }
}
