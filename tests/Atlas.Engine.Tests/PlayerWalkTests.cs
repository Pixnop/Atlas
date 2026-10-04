using System.Collections.Concurrent;
using Atlas.Internal.Bootstrap;
using Atlas.Internal.Player;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Pins <c>ITestPlayer.WalkTo</c> against CollisionFixtureMod, two blocks that log every
/// call a player makes on them: <c>solid</c>, a full cube, and <c>ghost</c>, a block with no
/// collision box (the shape of a pressure plate or a zone block). The point of a walk is that the
/// server runs its own collision pass for each step, so <c>Block.OnEntityCollide</c> fires,
/// which a teleport never does; the first two scenarios put the two side by side on the same
/// wall. Everything else is what the walk promises around that: a straight line at a fixed step
/// on the game thread, a stop at the first blockage with the position reached as the result, and
/// no exception for a blockage.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 717171, Mods = ["../../../../CollisionFixtureMod"])]
public class PlayerWalkTests : AtlasScenarioBase
{
    private ConcurrentQueue<string> EventLog => (ConcurrentQueue<string>)World.Api.ObjectCache["collisionfixture:events"];

    [AtlasScenario]
    public async Task WalkTo_Should_ReachTheMiddleOfTheTargetBlock_One_Step_PerTick_When_NothingIsInTheWay()
    {
        ITestPlayer player = await World.JoinPlayer("Walker1");
        BlockPos origin = Row(0);
        await Stand(player, origin);
        BlockPos target = origin.Offset(6, 0, 3);
        double distance = EngineCompat.SidedPosOf(player.Entity).XYZ.DistanceTo(new Vec3d(target.X + 0.5, target.Y, target.Z + 0.5));
        int before = World.CurrentTick;

        EntityPos reached = await player.WalkTo(target);

        Assert.Equal(target.X + 0.5, reached.X, precision: 6);
        Assert.Equal(target.Y, reached.Y, precision: 6);
        Assert.Equal(target.Z + 0.5, reached.Z, precision: 6);
        Assert.Equal(target, player.Position);

        // One step per tick, and one more tick after the last step, so the per-tick hooks have
        // seen the player at the place it stopped.
        Assert.Equal(WalkPath.StepCount(distance), World.CurrentTick - before);

        // A copy: the player moving later does not move it.
        EntityPos snapshot = reached.Copy();
        await player.WalkTo(origin);
        Assert.Equal(snapshot.X, reached.X);

        // Standing still after the walk, not carrying the last step's velocity.
        Assert.Equal(0, EngineCompat.SidedPosOf(player.Entity).Motion.Length(), precision: 9);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_StopAtTheFirstBlockage_And_FireOnEntityCollide_When_ASolidBlockIsInTheWay()
    {
        ITestPlayer player = await World.JoinPlayer("Walker2");
        BlockPos origin = Row(1);
        BlockPos wall = BuildWall(origin, distance: 6);
        await Stand(player, origin);
        double startZ = EngineCompat.SidedPosOf(player.Entity).Z;
        Drain();

        EntityPos reached = await player.WalkTo(origin.Offset(10, 0, 0));

        // The box is 0.6 wide, so it meets the wall when the middle of the player is 0.3 short of
        // it; the walk stops on the last step before that, up to one step short.
        Assert.InRange(reached.X, wall.X - 0.3 - WalkPath.StepBlocks - 1e-6, wall.X - 0.3 + 1e-3);
        Assert.Equal(startZ, reached.Z, precision: 6);
        Assert.Equal(origin.Y, reached.Y, precision: 6);
        Assert.Equal(origin.X + 5, player.Position.X);
        Assert.Equal(0, EngineCompat.SidedPosOf(player.Entity).Motion.Length(), precision: 9);

        string[] events = Drain();
        Assert.Contains(events, e => e.StartsWith($"collide|solid|{wall.X},{wall.Y},", StringComparison.Ordinal) && e.EndsWith("|west", StringComparison.Ordinal));
        Assert.DoesNotContain(events, e => e.StartsWith("collide|ghost", StringComparison.Ordinal));

        // Blocked from the first step now: the same position comes back, and the wall is hit again.
        EntityPos again = await player.WalkTo(origin.Offset(10, 0, 0));

        Assert.Equal(reached.X, again.X, precision: 9);
        Assert.Equal(reached.Z, again.Z, precision: 9);
        Assert.Contains(Drain(), e => e.StartsWith("collide|solid|", StringComparison.Ordinal));
    }

    [AtlasScenario]
    public async Task OnEntityCollide_Should_FireForAWalk_And_NotForATeleport_When_TheSameWallIsMet()
    {
        ITestPlayer player = await World.JoinPlayer("Walker3");
        BlockPos origin = Row(2);
        BlockPos wall = BuildWall(origin, distance: 6);
        Drain();

        // Teleported flush against the wall, onto its face and into it: the player's box overlaps
        // the wall in the last case, so OnEntityInside is called every tick, and no teleport
        // gets a collision call.
        foreach (BlockPos spot in new[] { wall.Offset(-1, 0, 0), wall.Offset(0, 1, 0), wall })
        {
            await player.TeleportTo(spot);
            await World.Ticks(3);
        }

        string[] teleported = Drain();
        Assert.DoesNotContain(teleported, e => e.StartsWith("collide|", StringComparison.Ordinal));
        Assert.Contains(teleported, e => e.StartsWith("inside|solid|", StringComparison.Ordinal));

        await player.TeleportTo(origin);
        await player.WalkTo(origin);
        Drain();
        await player.WalkTo(origin.Offset(10, 0, 0));

        Assert.Contains(Drain(), e => e.StartsWith("collide|solid|", StringComparison.Ordinal));
    }

    [AtlasScenario]
    public async Task WalkTo_Should_PassThroughABlockWithNoCollisionBox_And_FireOnEntityInside_When_ItIsOnThePath()
    {
        ITestPlayer player = await World.JoinPlayer("Walker4");
        BlockPos origin = Row(3);
        BlockPos plate = origin.Offset(3, 0, 0);
        World.SetBlock("collisionfixture:ghost", plate);
        await Stand(player, origin);
        Drain();

        EntityPos reached = await player.WalkTo(origin.Offset(7, 0, 0));

        Assert.Equal(origin.X + 7.5, reached.X, precision: 6);
        string[] events = Drain();
        Assert.Contains(events, e => e == $"inside|ghost|{plate.X},{plate.Y},{plate.Z}");
        Assert.DoesNotContain(events, e => e.StartsWith("collide|", StringComparison.Ordinal));
    }

    [AtlasScenario]
    public async Task WalkTo_Should_RegisterThePlayerInTheChunkItEnds_When_TheWalkCrossesAChunkBorder()
    {
        ITestPlayer player = await World.JoinPlayer("Walker5");

        // The spawn sits on a chunk corner: the walk starts in the chunk before the border and
        // ends one block into the next.
        BlockPos before = Row(4).Offset(-1, 0, 0);
        await Stand(player, before);
        long indexBefore = player.Entity.InChunkIndex3d;
        Assert.Equal(IndexOf(player.Entity), indexBefore);

        await player.WalkTo(before.Offset(1, 0, 0));

        Assert.NotEqual(indexBefore, IndexOf(player.Entity));
        Assert.Equal(IndexOf(player.Entity), player.Entity.InChunkIndex3d);
        Assert.Contains(World.EntitiesIn(player.Position.Area(2)), e => e.EntityId == player.Entity.EntityId);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_StopOnItsFirstStep_When_ItStartsFlushAgainstABlockAfterTheEntityWasMovedByHand()
    {
        ITestPlayer player = await World.JoinPlayer("Walker8");
        BlockPos origin = Row(7);
        BlockPos wall = BuildWall(origin, distance: 6);

        // The physics behind a walk remembers the last position it saw. Walk far from the wall,
        // then move the entity next to it by hand, the way a mod or a scenario does, and walk
        // into it: the first step has to be measured from where the player now is, not from
        // where it last walked, or the wall is missed and the player ends up inside it.
        await Stand(player, origin.Offset(100, 0, 0));
        EntityPos position = EngineCompat.SidedPosOf(player.Entity);
        position.SetPos(wall.X - 0.3 - 0.05, origin.Y, origin.Z + 0.5);
        EngineCompat.PosOf(player.Entity).SetFrom(position);
        double flushX = position.X;
        Drain();

        EntityPos reached = await player.WalkTo(origin.Offset(10, 0, 0));

        Assert.Equal(flushX, reached.X, precision: 6);
        Assert.Contains(Drain(), e => e.StartsWith("collide|solid|", StringComparison.Ordinal));
    }

    [AtlasScenario]
    public async Task WalkTo_Should_StopAtAClosedDoor_And_PassItOnceItIsOpened_When_TheDoorIsAVanillaOne()
    {
        ITestPlayer player = await World.JoinPlayer("Walker9");
        BlockPos origin = Row(8);
        BlockPos door = origin.Offset(0, 0, 4);
        World.SetBlock("game:door-solid-aged", door);
        await Stand(player, origin);
        BlockPos beyond = origin.Offset(0, 0, 8);

        EntityPos shut = await player.WalkTo(beyond);

        // A closed door is a slab at the far side of its block (0.88 to 1.0 deep): the player
        // stops inside the block, one step or less before the slab.
        Assert.Equal(door, player.Position);
        Assert.InRange(shut.Z, door.Z + 0.88 - 0.3 - WalkPath.StepBlocks - 1e-6, door.Z + 0.88 - 0.3 + 1e-3);

        // Opened the way a right click does it, on the block the player looks at: the slab swings
        // to the side of the block and the same walk goes on through.
        player.LookAt(door, BlockFacing.NORTH);
        Block block = World.BlockAt(door);
        Assert.True(block.OnBlockInteractStart(World.Api.World, player.Player, player.Player.CurrentBlockSelection));
        await World.Ticks(5);

        EntityPos open = await player.WalkTo(beyond);

        Assert.Equal(beyond.Z + 0.5, open.Z, precision: 6);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_ReturnWhereItIs_When_TheTargetIsTheBlockItStandsIn()
    {
        ITestPlayer player = await World.JoinPlayer("Walker6");
        BlockPos origin = Row(5);
        await Stand(player, origin);
        int before = World.CurrentTick;

        EntityPos reached = await player.WalkTo(origin);

        Assert.Equal(origin.X + 0.5, reached.X, precision: 6);
        Assert.Equal(0, World.CurrentTick - before);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_RejectATargetItCannotWalkTo()
    {
        ITestPlayer player = await World.JoinPlayer("Walker7");
        BlockPos origin = Row(6);
        await Stand(player, origin);

        // Thrown from the call itself, not as a faulted task, and the player has not moved.
        Assert.IsType<ArgumentNullException>(Record.Exception(() => { _ = player.WalkTo(null!); }));
        BlockPos otherDimension = new(origin.X, origin.Y, origin.Z, 1);
        Exception? wrongDimension = Record.Exception(() => { _ = player.WalkTo(otherDimension); });
        Assert.IsType<ArgumentException>(wrongDimension);
        Assert.Contains("dimension", wrongDimension.Message, StringComparison.Ordinal);
        Assert.Equal(origin, player.Position);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_GoThroughASolidBlock_And_NotFireOnEntityCollide_When_ThePlayerIsInNoClip()
    {
        ITestPlayer player = await World.JoinPlayer("Walker10");
        BlockPos origin = Row(9);
        BlockPos wall = BuildWall(origin, distance: 6);
        await Stand(player, origin);
        BlockPos target = origin.Offset(10, 0, 0);
        player.Entity.Controls.NoClip = true;
        Drain();

        EntityPos reached = await player.WalkTo(target);

        // The engine skips its collision pass for a no-clip player, so nothing stops the walk and
        // OnEntityCollide never fires. It also moves the player by the step's velocity itself, on
        // top of the step, so the last step ends one step past the destination.
        Assert.True(reached.X > wall.X + 1);
        Assert.DoesNotContain(Drain(), e => e.StartsWith("collide|", StringComparison.Ordinal));
        Assert.Equal(target.X + 0.5 + WalkPath.StepBlocks, reached.X, precision: 6);
        Assert.Equal(origin.Y, reached.Y, precision: 6);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_SinkIntoTheFloorAndStopShort_When_TheDestinationIsLower()
    {
        ITestPlayer player = await World.JoinPlayer("Walker11");
        BlockPos origin = Row(10);
        await Stand(player, origin);
        BlockPos target = origin.Offset(3, -1, 0);

        EntityPos reached = await player.WalkTo(target);

        // A walk to another height is outside the contract. Nothing holds the player to the floor:
        // the first step goes below its surface, and the second is reported as a blockage, so the
        // walk ends in the floor, far from the destination.
        Assert.True(reached.Y < origin.Y);
        Assert.True(reached.Y > target.Y);
        Assert.True(reached.X < target.X);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_LiftThePlayerOffTheFloorAndStopAtTheBlock_When_TheDestinationIsHigher()
    {
        ITestPlayer player = await World.JoinPlayer("Walker12");
        BlockPos origin = Row(11);
        for (int dx = 3; dx <= 6; dx++)
        {
            World.SetBlock("collisionfixture:solid", origin.Offset(dx, 0, 0));
        }

        await Stand(player, origin);

        EntityPos reached = await player.WalkTo(origin.Offset(6, 1, 0));

        // Nothing steps the player up onto the block, and no gravity brings it back: it ends in
        // the air, in front of the block's side.
        Assert.True(reached.Y > origin.Y);
        Assert.True(reached.X < origin.X + 3);
    }

    /// <summary>The start of a lane of its own for one scenario: the class shares one world and
    /// does not roll it back, so what a scenario builds stays there for the next one, and lanes six
    /// blocks apart keep a wall (three wide) out of another scenario's way. Lane 4 crosses the
    /// chunk border the spawn sits on, in X.</summary>
    private BlockPos Row(int lane) => World.Spawn.Offset(0, 1, 6 * lane);

    private long IndexOf(Entity entity) => World.Api.World.ChunkProvider.ChunkIndex3D(EngineCompat.SidedPosOf(entity));

    /// <summary>Puts the player in the middle of <paramref name="origin"/>: a teleport lands on
    /// the block's corner, and the walks below are measured from the centre.</summary>
    private static async Task Stand(ITestPlayer player, BlockPos origin)
    {
        await player.TeleportTo(origin);
        await player.WalkTo(origin);
    }

    /// <summary>Builds a wall of solid fixture blocks, three wide and two high, across the line
    /// east of <paramref name="origin"/>, and returns the position of its middle bottom
    /// block.</summary>
    private BlockPos BuildWall(BlockPos origin, int distance)
    {
        BlockPos middle = origin.Offset(distance, 0, 0);
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dy = 0; dy <= 1; dy++)
            {
                World.SetBlock("collisionfixture:solid", middle.Offset(0, dy, dz));
            }
        }

        return middle;
    }

    private string[] Drain()
    {
        var lines = new List<string>();
        while (EventLog.TryDequeue(out string? line))
        {
            lines.Add(line);
        }

        return [.. lines];
    }
}
