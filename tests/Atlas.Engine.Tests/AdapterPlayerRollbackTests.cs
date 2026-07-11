using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Covers player-aware rollback (spec stage 2, issue #47) end-to-end through the real
/// xUnit adapter, in the shape the Manifold scenarios need: scenario A joins the class player
/// and sets its baseline; scenario B is the class's first rollback-enabled scenario, so its lazy
/// capture happens WITH the player joined (a setup error before stage 2), and then pollutes both
/// the player and a SaveGame event flag (the one-shot-assert case); scenario C requests a
/// rollback and must see the captured baseline: player position, inventory, per-player moddata
/// and the event flag all reset, with the player still connected. The orderer makes the
/// A-then-B-then-C sequence deterministic; the static fields carry the shared player across
/// scenarios (xUnit news up the class per scenario; joined players are host-scoped).</summary>
[TestCaseOrderer("Atlas.Engine.Tests.AlphabeticalOrderer", "Atlas.Engine.Tests")]
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 474747)]
public class AdapterPlayerRollbackTests : AtlasScenarioBase
{
    private const string FlagKey = "adapter-player-rollback";

    private static ITestPlayer? _steve;
    private static BlockPos? _baselinePos;
    private static int _hotbarSlot;

    [AtlasScenario]
    public async Task A_Scenario_Should_JoinThePlayerAndSetItsBaseline_When_ClassStarts()
    {
        _steve = await World.JoinPlayer("RollbackSteve");
        await _steve.GiveItem("game:flint", 1);
        _steve.Player.SetModdata(FlagKey, new byte[] { 1 });
        await World.Ticks(2);
        _baselinePos = _steve.Position;
        _hotbarSlot = _steve.Player.InventoryManager.ActiveHotbarSlotNumber;
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task B_Scenario_Should_CaptureWithPlayerJoinedThenPollute_When_FirstRollbackScenarioRuns()
    {
        Assert.NotNull(_steve); // the orderer ran A first
        Assert.True(_steve!.IsConnected, "the player joined by A must still be connected");
        Assert.Null(World.Api.WorldManager.SaveGame.GetData(FlagKey)); // clean at capture time

        await _steve.GiveItem("game:flint", 8);
        _steve.Player.SetModdata(FlagKey, new byte[] { 9 });
        World.Api.WorldManager.SaveGame.StoreData(FlagKey, new byte[] { 42 });
        await _steve.TeleportTo(_baselinePos!.Offset(24, 4, 24));
        await World.Ticks(2);

        Assert.NotEqual(_baselinePos, _steve.Position); // the pollution really landed
    }

    [AtlasScenario(RollbackWorld = true)]
    public async Task C_Scenario_Should_SeeSnapshotPlayerAndWorld_When_RollbackWorldIsRequested()
    {
        Assert.NotNull(_steve); // the orderer ran A and B first
        await World.Ticks(1);

        Assert.True(_steve!.IsConnected, "the captured player must survive rollbacks");
        Assert.Equal(_baselinePos, _steve.Position); // B's teleport is undone

        ItemSlot slot = _steve.Player.InventoryManager.GetHotbarInventory()[_hotbarSlot];
        Assert.Equal("game:flint", slot.Itemstack?.Collectible?.Code.ToString());
        Assert.Equal(1, slot.Itemstack!.StackSize); // B's duplicate items are gone

        Assert.Equal(new byte[] { 1 }, _steve.Player.GetModdata(FlagKey)); // per-player moddata reset
        Assert.Null(World.Api.WorldManager.SaveGame.GetData(FlagKey)); // the event flag is no longer one-shot
    }
}
