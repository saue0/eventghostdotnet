using EventGhostDotNet.Core.Updates;
using Xunit;

namespace EventGhostDotNet.Core.Tests;

public class UpdateVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v2.0.1", 2, 0, 1)]
    [InlineData("V0.9.12", 0, 9, 12)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    [InlineData(" 1.2.3+build.7 ", 1, 2, 3)]
    public void TryParse_AccepteLesEtiquettesDePublication(string text, int major, int minor, int patch)
    {
        Assert.True(UpdateVersion.TryParse(text, out var version));
        Assert.Equal(new UpdateVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("-1.2.3")]
    [InlineData("version 1.2.3")]
    public void TryParse_RefuseLesFormatsInvalides(string? text)
    {
        Assert.False(UpdateVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.9", "1.1.0")]
    [InlineData("1.9.0", "2.0.0")]
    public void CompareTo_ClasseParNumero(string older, string newer)
    {
        Assert.True(UpdateVersion.TryParse(older, out var first));
        Assert.True(UpdateVersion.TryParse(newer, out var second));

        Assert.True(second.CompareTo(first) > 0);
        Assert.True(first.CompareTo(second) < 0);
        Assert.Equal(0, first.CompareTo(first));
    }

    [Fact]
    public void ToString_UtiliseTroisComposants()
    {
        Assert.Equal("1.2.0", new UpdateVersion(1, 2, 0).ToString());
    }
}
