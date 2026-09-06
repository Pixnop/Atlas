using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

/// <summary>The collection serializes this class with HostRegistryConcurrencyTests: harvesting
/// enters the registry's process-wide gate, which that class deliberately holds busy.</summary>
[Collection("HostRegistry")]
public class FixtureHarvestTests
{
    [Fact]
    public void ShutDownAndHarvestSavePath_Should_ReturnNullWithoutError_When_NoHostIsLive()
    {
        // With no scenario ever run in this process there is no live host, which must read as
        // "nothing to harvest", not as a failure. The seam's shape needs no test of its own any
        // more: the CLI compiles against it, so a rename fails the build.
        string? savePath = FixtureHarvest.ShutDownAndHarvestSavePath(out string? error);

        Assert.Null(error);
        Assert.Null(savePath);
    }
}
