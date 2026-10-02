using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit;

namespace Atlas.Staging.Scenarios;

/// <summary>Two folder mods staged by the AtlasMod sugar alone. Each ships a block with its shape
/// and texture under the project's own assets/ folder; neither project copies that folder to its
/// build output, so a mod that loads with its block present proves the sugar staged the project's
/// assets next to the build.</summary>
[Trait("Category", "E2E")]
public class FolderModStagingScenarios : AtlasScenarioBase
{
    private static readonly string OutputDir = Path.GetDirectoryName(typeof(FolderModStagingScenarios).Assembly.Location)!;

    [AtlasTheory]
    [InlineData("stagingalpha")]
    [InlineData("stagingbeta")]
    public Task FolderMod_Should_Load_When_ReferencedWithAtlasMod(string modId)
    {
        Assert.True(World.Api.ModLoader.IsModEnabled(modId), $"mod '{modId}' is not loaded");
        return Task.CompletedTask;
    }

    [AtlasTheory]
    [InlineData("stagingalpha")]
    [InlineData("stagingbeta")]
    public Task Block_Should_Exist_When_ItsAssetsSitAtTheProjectRoot(string modId)
    {
        Block? block = World.Api.World.GetBlock(new AssetLocation(modId, "marker"));

        Assert.NotNull(block);
        Assert.NotEqual(0, block.Id);
        Assert.NotNull(World.Api.Assets.TryGet(new AssetLocation(modId, "shapes/block/marker.json")));
        Assert.NotNull(World.Api.Assets.TryGet(new AssetLocation(modId, "textures/block/marker.png")));
        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task OutputOnlyAsset_Should_BeKept_And_ProjectCopyShouldWin_When_BothHoldTheSameAsset()
    {
        // WriteOutputOnlyAssets in the Alpha fixture put both into the build output.
        Assert.NotNull(World.Api.Assets.TryGet(new AssetLocation("stagingalpha", "config/generated.json")));
        Block? block = World.Api.World.GetBlock(new AssetLocation("stagingalpha", "marker"));
        Assert.NotNull(block);
        Assert.Equal(1.5f, block.Resistance);
        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task StagedFolder_Should_NotCarryAnotherModsDll_When_ItsBuildOutputHoldsOne()
    {
        // The Beta fixture references Alpha, so its build output holds StagingFixtureMod.Alpha.dll.
        string betaFolder = Path.Combine(OutputDir, "atlas-mods", "StagingFixtureBeta");

        Assert.Equal(
            new[] { "StagingFixtureBeta.dll" },
            Directory.GetFiles(betaFolder, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray());
        Assert.Empty(Directory.GetFiles(betaFolder, "StagingFixtureMod.Alpha.*", SearchOption.AllDirectories));
        return Task.CompletedTask;
    }

    [AtlasScenario]
    public Task Manifest_Should_NameEachFolderAfterItsAssembly_When_BothOutputsAreCalledNet10()
    {
        string[] lines = File.ReadAllLines(Path.Combine(OutputDir, "atlas-mods.generated.txt"))
            .Where(line => line.Trim().Length > 0)
            .ToArray();

        Assert.Equal(
            new[] { "StagingFixtureBeta", "StagingFixtureMod.Alpha" },
            lines.Select(line => Path.GetFileName(line.TrimEnd('/', '\\'))).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.All(lines, line => Assert.True(Directory.Exists(line), $"manifest names a folder that is not there: {line}"));
        return Task.CompletedTask;
    }
}
