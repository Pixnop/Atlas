using Atlas.Cli;
using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Cli;

/// <summary>The CLI runs its own copy of the VINTAGE_STORY check, because calling
/// VsInstall.Validate would make it load an Atlas.dll it does not ship. These pin the copy to
/// the original: same verdict and, when there is one, the same sentence.</summary>
public class VintageStoryEnvironmentTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("/opt/empty", false)]
    [InlineData("/opt/vs", true)]
    public void Validate_Should_AgreeWithVsInstall_When_GivenTheSameInstall(string? directory, bool libPresent)
    {
        Assert.Equal(
            VsInstall.Validate(directory, _ => libPresent),
            VintageStoryEnvironment.Validate(directory, _ => libPresent));
    }

    [Fact]
    public void Validate_Should_ReturnErrorNamingTheValue_When_InstallIsIncomplete()
    {
        string? error = VintageStoryEnvironment.Validate("/opt/empty", _ => false);

        Assert.NotNull(error);
        Assert.Contains("/opt/empty", error);
        Assert.Contains("VintagestoryLib.dll", error);
    }

    [Fact]
    public void Validate_Should_ProbeForVintagestoryLib_When_DirectoryIsGiven()
    {
        string? probed = null;

        VintageStoryEnvironment.Validate("/opt/vs", path =>
        {
            probed = path;
            return true;
        });

        Assert.Equal(Path.Combine("/opt/vs", "VintagestoryLib.dll"), probed);
    }
}
