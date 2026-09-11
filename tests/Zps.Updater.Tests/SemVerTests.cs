using Zps.Updater;

namespace Zps.Updater.Tests;

public class SemVerTests
{
    [Theory]
    [InlineData("v7.0.1", 7, 0, 1, 0)]
    [InlineData("V7.0.1", 7, 0, 1, 0)]
    [InlineData("7.0.1", 7, 0, 1, 0)]
    [InlineData("v7.0", 7, 0, 0, 0)]
    [InlineData("v7", 7, 0, 0, 0)]
    [InlineData("v7.0.1.2", 7, 0, 1, 2)]
    public void TryParse_FormatosValidos_DevuelveVersionCorrecta(string tag, int mayor, int menor, int build, int revision)
    {
        var ok = SemVer.TryParse(tag, out var version);

        Assert.True(ok);
        Assert.Equal(new Version(mayor, menor, build, revision), version);
    }

    [Theory]
    [InlineData("v7.0.1-beta.1", 7, 0, 1)]
    [InlineData("v7.0.1+build.5", 7, 0, 1)]
    [InlineData("7.0.1-rc1+exp.sha.5114f85", 7, 0, 1)]
    public void TryParse_DescartaSufijosDePrereleaseYMetadata(string tag, int mayor, int menor, int build)
    {
        var ok = SemVer.TryParse(tag, out var version);

        Assert.True(ok);
        Assert.Equal(mayor, version.Major);
        Assert.Equal(menor, version.Minor);
        Assert.Equal(build, version.Build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-una-version")]
    [InlineData("v7.0.1.2.3")]
    [InlineData("v.")]
    [InlineData("v7..1")]
    public void TryParse_FormatosInvalidos_DevuelveFalse(string? tag)
    {
        var ok = SemVer.TryParse(tag, out _);

        Assert.False(ok);
    }

    [Fact]
    public void EsMasNueva_ComparaCorrectamente()
    {
        Assert.True(SemVer.EsMasNueva(new Version(7, 1, 0, 0), new Version(7, 0, 1, 0)));
        Assert.False(SemVer.EsMasNueva(new Version(7, 0, 1, 0), new Version(7, 0, 1, 0)));
        Assert.False(SemVer.EsMasNueva(new Version(6, 9, 9, 9), new Version(7, 0, 0, 0)));
    }
}
