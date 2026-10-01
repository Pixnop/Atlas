using Atlas.Internal.Bootstrap;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Covers <c>IWorldSession.PositionOf</c> and <c>WaitForPosition</c> through the real
/// xUnit adapter: the read is a copy of the server-side position, and the wait is
/// <c>Until</c> over that read, so it sees a move the pass it happens in (the entity lookup
/// <c>EntitiesIn</c> only sees it once the engine's once-a-second pass has re-indexed the
/// entity), can test the dimension, and faults the way <c>Until</c> does.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 616161)]
public class EntityPositionTests : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task PositionOf_Should_ReturnACopyOfTheServerSidePosition_When_TheEntityIsSpawned()
    {
        BlockPos spawnAt = World.Spawn.Offset(3, 1, 0);
        Entity chicken = World.SpawnEntity("game:chicken-rooster", spawnAt);

        EntityPos read = World.PositionOf(chicken);

        Assert.NotSame(EngineCompat.SidedPosOf(chicken), read);
        Assert.Equal(spawnAt.X, read.X);
        Assert.Equal(spawnAt.Z, read.Z);

        // A copy in both directions: writing to it moves nothing, and the entity moving later
        // leaves it where it was read.
        read.X += 100;
        Assert.Equal(spawnAt.X, EngineCompat.SidedPosOf(chicken).X);

        EntityPos before = World.PositionOf(chicken);
        EngineCompat.SidedPosOf(chicken).X += 5;
        Assert.Equal(spawnAt.X, before.X);
        Assert.Equal(spawnAt.X + 5, World.PositionOf(chicken).X);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task WaitForPosition_Should_ReturnTheArrivalWithinTwoPasses_When_TheEngineTeleportsACreature()
    {
        BlockPos spawnAt = World.Spawn.Offset(3, 1, 0);
        Entity chicken = World.SpawnEntity("game:chicken-rooster", spawnAt);
        await World.Ticks(2);

        // 70 blocks away: another chunk column, which the engine's teleport loads first and then
        // moves the entity, calling back on the pass that did it.
        var target = new EntityPos(spawnAt.X + 70, World.Spawn.Y + 1, spawnAt.Z);
        int appliedAtTick = -1;
        chicken.TeleportTo(target, () => appliedAtTick = World.CurrentTick);

        EntityPos arrival = await World.WaitForPosition(chicken, p => p.DistanceTo(target.XYZ) < 1);

        Assert.True(appliedAtTick >= 0, "the engine's onTeleported callback had not fired when the wait returned");
        Assert.True(
            World.CurrentTick - appliedAtTick <= 2,
            $"the wait took {World.CurrentTick - appliedAtTick} passes to see a move the callback reported");
        Assert.Equal(target.X, arrival.X, precision: 0);
        Assert.Equal(target.Z, arrival.Z, precision: 0);
    }

    [AtlasScenario]
    public async Task WaitForPosition_Should_ReturnTheSnapshotOfTheFirstMatchingPass_When_TheDimensionChanges()
    {
        BlockPos spawnAt = World.Spawn.Offset(3, 1, 0);
        Entity chicken = World.SpawnEntity("game:chicken-rooster", spawnAt);
        await World.Ticks(2);

        // What a mod's own re-homing does: the same entity instance, a different dimension.
        // Nothing else moves, so the predicate is about the dimension alone.
        int flippedAtTick = World.CurrentTick + 3;
        long listener = World.Api.Event.RegisterGameTickListener(
            _ =>
            {
                if (World.CurrentTick >= flippedAtTick)
                {
                    EngineCompat.SidedPosOf(chicken).Dimension = 1;
                }
            },
            1);
        try
        {
            EntityPos arrival = await World.WaitForPosition(chicken, p => p.Dimension == 1);

            Assert.Equal(1, arrival.Dimension);
            Assert.True(World.CurrentTick >= flippedAtTick, "the wait returned before the dimension changed");

            // A snapshot: the entity going back does not rewrite what the wait returned.
            EngineCompat.SidedPosOf(chicken).Dimension = 0;
            Assert.Equal(1, arrival.Dimension);
        }
        finally
        {
            World.Api.Event.UnregisterGameTickListener(listener);
            EngineCompat.SidedPosOf(chicken).Dimension = 0;
        }
    }

    [AtlasScenario]
    public async Task WaitForPosition_Should_FailWithScenarioTimeout_When_TheEntityNeverArrives()
    {
        Entity chicken = World.SpawnEntity("game:chicken-rooster", World.Spawn.Offset(3, 1, 0));

        ScenarioTimeoutException timeout = await Assert.ThrowsAsync<ScenarioTimeoutException>(
            () => World.WaitForPosition(chicken, p => p.Y > 10000, timeoutTicks: 4));

        Assert.Equal(4, timeout.TicksWaited);
    }

    [AtlasScenario]
    public async Task WaitForPosition_Should_FailWithThePredicateException_When_ThePredicateThrows()
    {
        Entity chicken = World.SpawnEntity("game:chicken-rooster", World.Spawn.Offset(3, 1, 0));
        var boom = new InvalidOperationException("the predicate read something that was gone");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => World.WaitForPosition(chicken, _ => throw boom));

        Assert.Same(boom, thrown);
    }

    [AtlasScenario]
    public async Task WaitForPosition_Should_RejectNullArguments_And_AnEmptyTimeout()
    {
        Entity chicken = World.SpawnEntity("game:chicken-rooster", World.Spawn.Offset(3, 1, 0));

        // Thrown from the call itself, like Until's: not wrapped in a faulted task.
        Assert.Throws<ArgumentNullException>(() => World.PositionOf(null!));
        Assert.IsType<ArgumentNullException>(Record.Exception(() => { _ = World.WaitForPosition(null!, _ => true); }));
        Assert.IsType<ArgumentNullException>(Record.Exception(() => { _ = World.WaitForPosition(chicken, null!); }));
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => { _ = World.WaitForPosition(chicken, _ => true, timeoutTicks: 0); }));
        await World.Ticks(1);
    }
}
