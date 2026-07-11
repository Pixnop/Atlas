using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class CliVersionTests
{
    [Fact]
    public void FromInformationalVersion_Should_StripBuildMetadata_When_ShaSuffixPresent()
    {
        Assert.Equal("0.6.0", CliVersion.FromInformationalVersion("0.6.0+8f3ab12c9d"));
    }

    [Fact]
    public void FromInformationalVersion_Should_PassThrough_When_NoBuildMetadata()
    {
        Assert.Equal("0.6.0", CliVersion.FromInformationalVersion("0.6.0"));
    }

    [Fact]
    public void FromInformationalVersion_Should_KeepPrerelease_When_StrippingMetadata()
    {
        Assert.Equal("0.7.0-beta.1", CliVersion.FromInformationalVersion("0.7.0-beta.1+8f3ab12c"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+8f3ab12c")]
    public void FromInformationalVersion_Should_ReturnUnknown_When_NoVersionSurvives(string? raw)
    {
        Assert.Equal(CliVersion.Unknown, CliVersion.FromInformationalVersion(raw));
    }

    [Fact]
    public void Resolve_Should_ReturnThePackageStyleVersionOfTheCliAssembly()
    {
        string version = CliVersion.Resolve();

        // The CLI assembly always carries an informational version (the SDK derives it from
        // <Version> in Directory.Build.props), so the fallback must never surface here, and
        // stripping the build metadata must leave a plain dotted version.
        Assert.NotEqual(CliVersion.Unknown, version);
        Assert.DoesNotContain("+", version);
        Assert.Matches(@"^\d+\.\d+", version);
    }
}
