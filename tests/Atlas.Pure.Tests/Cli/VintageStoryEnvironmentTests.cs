using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class VintageStoryEnvironmentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_Should_ReturnError_When_VariableIsUnset(string? directory)
    {
        string? error = VintageStoryEnvironment.Validate(directory, _ => true);

        Assert.NotNull(error);
        Assert.Contains("VINTAGE_STORY", error);
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

    [Fact]
    public void Validate_Should_ReturnNull_When_InstallLooksValid()
    {
        Assert.Null(VintageStoryEnvironment.Validate("/opt/vs", _ => true));
    }
}
