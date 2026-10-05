using Atlas.Internal.Bootstrap;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Pins <c>ITestPlayer.LookAt</c>. The server reads the selection from the player's aim
/// on every tick (<c>ServerSystemEntitySimulation.OnServerTick</c> traces it from the eye, along
/// the entity's yaw and pitch, over the player's picking range), so a selection written into
/// <c>Entity.BlockSelection</c> alone is gone one tick later. <c>LookAt</c> writes it and aims the
/// player at the block as well: the first scenarios show it is there at once, from
/// <c>CurrentBlockSelection</c> and from a command, and the next ones that it survives the
/// ticks when the block can be seen, and what happens when it cannot. One scenario pins the
/// engine behavior all of it rests on, a selection written by hand and nothing else.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 737373, Mods = ["PlayerCommandFixtureMod.dll"])]
public class PlayerLookAtTests : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task LookAt_Should_SelectTheBlockOnItsTopFace_When_NoFaceIsGiven()
    {
        ITestPlayer player = await World.JoinPlayer("Looker1");
        BlockPos origin = World.Spawn.Offset(0, 1, 0);
        await player.TeleportTo(origin);
        BlockPos block = origin.Offset(2, -1, 0);

        player.LookAt(block);

        BlockSelection? selection = player.Player.CurrentBlockSelection;
        Assert.NotNull(selection);
        Assert.Equal(block, selection.Position);
        Assert.Same(BlockFacing.UP, selection.Face);
        Assert.Equal(World.BlockAt(block).Code, selection.Block.Code);
        Assert.Equal(0.5, selection.HitPosition.X, precision: 6);
        Assert.Equal(1.0, selection.HitPosition.Y, precision: 6);
    }

    [AtlasScenario]
    public async Task LookAt_Should_SelectTheFaceGiven()
    {
        ITestPlayer player = await World.JoinPlayer("Looker2");
        BlockPos origin = World.Spawn.Offset(0, 1, 0);
        await player.TeleportTo(origin);
        BlockPos block = origin.Offset(2, -1, 0);

        player.LookAt(block, BlockFacing.NORTH);

        Assert.Same(BlockFacing.NORTH, player.Player.CurrentBlockSelection!.Face);
        Assert.Equal(block, player.Player.CurrentBlockSelection!.Position);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task LookAt_Should_BeWhatACommandReadsAsTheBlockTheCallerLooksAt()
    {
        ITestPlayer player = await World.JoinPlayer("Looker3");
        BlockPos origin = World.Spawn.Offset(0, 1, 0);
        await player.TeleportTo(origin);
        BlockPos block = origin.Offset(2, -1, 1);

        CommandResult before = await player.ExecuteCommand("/callerfx looking");
        player.LookAt(block, BlockFacing.UP);
        CommandResult during = await player.ExecuteCommand("/callerfx looking");

        Assert.Equal("none", before.Message);
        Assert.Equal($"{block.X},{block.Y},{block.Z}:up", during.Message);
    }

    [AtlasScenario]
    public async Task LookAt_Should_HoldTheSelectionAcrossTicks_When_TheBlockIsWithinSightAndReach()
    {
        // Not teleported first: before 1.22 a joined player's Entity.Pos is still at the origin,
        // and the trace that keeps the selection starts from it.
        ITestPlayer player = await World.JoinPlayer("Looker4");
        BlockPos feet = player.Position;
        BlockPos block = feet.Offset(2, -1, 1);

        player.LookAt(block);
        await World.Ticks(10);

        BlockSelection? selection = player.Player.CurrentBlockSelection;
        Assert.NotNull(selection);
        Assert.Equal(block, selection.Position);
        Assert.Same(BlockFacing.UP, selection.Face);

        // A command that goes through the chat path, a few ticks later, sees it too.
        await player.Say("/callerfx looking");
        Assert.Contains(player.Client.ChatLines(), l => l.Contains($"{block.X},{block.Y},{block.Z}:up", StringComparison.Ordinal));
    }

    [AtlasScenario]
    public async Task LookAt_Should_LeaveTheSelectionToTheEngine_When_AnotherBlockIsInTheWay()
    {
        ITestPlayer player = await World.JoinPlayer("Looker5");
        BlockPos origin = World.Spawn.Offset(0, 1, 12);
        await player.TeleportTo(origin);
        BlockPos hidden = origin.Offset(3, -1, 0);

        // The line from the eye to the top of the target passes through this block.
        BlockPos inTheWay = origin.Offset(2, 0, 0);
        World.SetBlock("game:soil-medium-normal", inTheWay);

        player.LookAt(hidden);

        // Written for the pass it was set in, and not what the engine's own trace finds afterwards.
        Assert.Equal(hidden, player.Player.CurrentBlockSelection!.Position);
        await World.Ticks(2);
        Assert.Equal(inTheWay, player.Player.CurrentBlockSelection?.Position);
    }

    [AtlasScenario]
    public async Task BlockSelection_Should_BeGoneAfterATick_When_ItIsWrittenWithoutAimingAtIt()
    {
        // What LookAt's aim is for: the server traces the selection again every tick, so one that
        // nothing aims at does not outlive the pass it was written in. If the engine ever keeps
        // it, this fails and the LookAt remarks that say otherwise want revising.
        ITestPlayer player = await World.JoinPlayer("Looker7");
        BlockPos origin = World.Spawn.Offset(0, 1, 18);
        await player.TeleportTo(origin);
        BlockPos block = origin.Offset(2, -1, 0);

        player.Entity.BlockSelection = new BlockSelection(block, BlockFacing.UP, World.BlockAt(block));
        Assert.Equal(block, player.Player.CurrentBlockSelection!.Position);
        await World.Ticks(2);

        Assert.Null(player.Player.CurrentBlockSelection);
    }

    [AtlasScenario]
    public async Task LookAt_Should_RejectANullPosition()
    {
        ITestPlayer player = await World.JoinPlayer("Looker6");

        Assert.Throws<ArgumentNullException>(() => player.LookAt(null!));
        await World.Ticks(1);
    }
}
