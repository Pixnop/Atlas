using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class FixtureOutputTests
{
    [Fact]
    public void Validate_Should_ReturnNull_When_FileDoesNotExist()
    {
        Assert.Null(FixtureOutput.Validate("fixtures/world.vcdbs", force: false, _ => false));
    }

    [Fact]
    public void Validate_Should_RefuseAndPointAtForce_When_FileExistsWithoutForce()
    {
        string? error = FixtureOutput.Validate("fixtures/world.vcdbs", force: false, _ => true);

        Assert.NotNull(error);
        Assert.Contains("refusing to overwrite", error);
        Assert.Contains("fixtures/world.vcdbs", error);
        Assert.Contains("--force", error);
    }

    [Fact]
    public void Validate_Should_ReturnNull_When_FileExistsButForceGiven()
    {
        Assert.Null(FixtureOutput.Validate("fixtures/world.vcdbs", force: true, _ => true));
    }
}
