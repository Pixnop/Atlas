using Atlas.Internal.Bootstrap;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Pins <c>ITestPlayer.Mount</c> and <c>Dismount</c> against a vanilla raft, which has
/// two seats, and a vanilla rooster, which has none: the player takes the first free seat the
/// entity's <c>IMountable</c> lists, a full entity answers <see langword="false"/> rather than
/// throwing, and both calls act in the same pass. <c>Mount</c> leaves the player where it is;
/// <c>Dismount</c> does not: a boat's seat puts the player at the free spot nearest to it.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 727272)]
public class PlayerMountTests : AtlasScenarioBase
{
    private const string Raft = "game:boat-raft-aged";

    [AtlasScenario]
    public async Task Mount_Should_SeatThePlayerInTheFirstFreeSeat_And_Dismount_Should_FreeIt()
    {
        ITestPlayer player = await World.JoinPlayer("Rider1");
        Entity raft = World.SpawnEntity(Raft, World.Spawn.Offset(3, 1, 0));
        await World.Ticks(2);
        IMountableSeat[] seats = raft.GetInterface<IMountable>()!.Seats;

        Assert.True(player.Mount(raft));

        Assert.Same(seats[0], player.Entity.MountedOn);
        Assert.Same(player.Entity, seats[0].Passenger);
        Assert.Null(seats[1].Passenger);
        await World.Ticks(3);
        Assert.Same(seats[0], player.Entity.MountedOn);

        Assert.True(player.Dismount());

        Assert.Null(player.Entity.MountedOn);
        Assert.Null(seats[0].Passenger);
    }

    [AtlasScenario]
    public async Task Dismount_Should_PutThePlayerAtTheNearestFreeSpotAroundItself_And_Mount_Should_NotMoveIt_When_TheSeatIsABoats()
    {
        ITestPlayer corner = await World.JoinPlayer("Rider8");
        ITestPlayer middle = await World.JoinPlayer("Rider9");
        ITestPlayer far = await World.JoinPlayer("Rider10");
        BlockPos at = World.Spawn.Offset(-40, 1, 40);
        Entity raft = World.SpawnEntity(Raft, at);
        await World.Ticks(2);

        // A teleport lands on the block's corner. The boat's search runs around the player's own
        // position, not the boat's, and the free spots around a corner are equally near: the
        // player moves about half a block on each axis, to the middle of a block, and 0.1 up.
        await corner.TeleportTo(at);
        Assert.True(corner.Mount(raft));
        await World.Ticks(3);
        Assert.Equal(at, corner.Position);
        Assert.True(corner.Dismount());
        EntityPos cornerLanded = EngineCompat.SidedPosOf(corner.Entity);
        double moved = Math.Sqrt(Math.Pow(cornerLanded.X - at.X, 2) + Math.Pow(cornerLanded.Z - at.Z, 2));
        Assert.InRange(moved, 0.5, 0.75);
        Assert.Equal(at.Y + 0.1, cornerLanded.Y, precision: 6);
        Assert.Equal(0.5, Math.Abs(cornerLanded.X - Math.Floor(cornerLanded.X)), precision: 6);
        Assert.Equal(0.5, Math.Abs(cornerLanded.Z - Math.Floor(cornerLanded.Z)), precision: 6);

        // Already in the middle of its block, the player stays there and rises 0.1.
        await middle.TeleportTo(at);
        await middle.WalkTo(at);
        Assert.True(middle.Mount(raft));
        Assert.True(middle.Dismount());
        EntityPos middleLanded = EngineCompat.SidedPosOf(middle.Entity);
        Assert.Equal(at.X + 0.5, middleLanded.X, precision: 6);
        Assert.Equal(at.Y + 0.1, middleLanded.Y, precision: 6);
        Assert.Equal(at.Z + 0.5, middleLanded.Z, precision: 6);

        // A player far from the boat is not taken to it.
        BlockPos away = at.Offset(0, 0, 12);
        await far.TeleportTo(away);
        await far.WalkTo(away);
        Assert.True(far.Mount(raft));
        Assert.True(far.Dismount());
        EntityPos farLanded = EngineCompat.SidedPosOf(far.Entity);
        Assert.Equal(away.X + 0.5, farLanded.X, precision: 6);
        Assert.Equal(away.Z + 0.5, farLanded.Z, precision: 6);
    }

    [AtlasScenario]
    public async Task Mount_Should_TakeTheNextSeat_When_TheFirstIsTaken_And_ReturnFalse_When_NoSeatIsFree()
    {
        ITestPlayer first = await World.JoinPlayer("RiderOne2");
        ITestPlayer second = await World.JoinPlayer("RiderTwo2");
        ITestPlayer third = await World.JoinPlayer("RiderThree2");
        Entity raft = World.SpawnEntity(Raft, World.Spawn.Offset(3, 1, 0));
        await World.Ticks(2);
        IMountableSeat[] seats = raft.GetInterface<IMountable>()!.Seats;

        Assert.True(first.Mount(raft));
        Assert.True(second.Mount(raft));

        Assert.Same(seats[0], first.Entity.MountedOn);
        Assert.Same(seats[1], second.Entity.MountedOn);

        Assert.False(third.Mount(raft));
        Assert.Null(third.Entity.MountedOn);

        // The seat the first player leaves is the one the third takes.
        Assert.True(first.Dismount());
        Assert.True(third.Mount(raft));
        Assert.Same(seats[0], third.Entity.MountedOn);
        Assert.Same(seats[1], second.Entity.MountedOn);
    }

    [AtlasScenario]
    public async Task Mount_Should_ReturnTrueAndStayWhereItSits_When_ThePlayerIsAlreadyOnThatEntity()
    {
        ITestPlayer player = await World.JoinPlayer("Rider3");
        ITestPlayer other = await World.JoinPlayer("Passenger3");
        Entity raft = World.SpawnEntity(Raft, World.Spawn.Offset(3, 1, 0));
        await World.Ticks(2);
        IMountableSeat[] seats = raft.GetInterface<IMountable>()!.Seats;
        Assert.True(player.Mount(raft));
        Assert.True(other.Mount(raft));

        // Both seats are taken, and the player holds one of them.
        Assert.True(player.Mount(raft));

        Assert.Same(seats[0], player.Entity.MountedOn);
        Assert.Same(seats[1], other.Entity.MountedOn);
    }

    [AtlasScenario]
    public async Task Mount_Should_ReturnFalse_When_TheEntityHasNoSeat()
    {
        ITestPlayer player = await World.JoinPlayer("Rider4");
        Entity rooster = World.SpawnEntity("game:chicken-rooster", World.Spawn.Offset(3, 1, 0));
        await World.Ticks(2);

        Assert.False(player.Mount(rooster));

        Assert.Null(player.Entity.MountedOn);
    }

    [AtlasScenario]
    public async Task Dismount_Should_ReturnTrue_When_ThePlayerIsNotMounted()
    {
        ITestPlayer player = await World.JoinPlayer("Rider5");

        Assert.True(player.Dismount());

        Assert.Null(player.Entity.MountedOn);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task WalkTo_Should_Throw_When_ThePlayerIsMounted()
    {
        ITestPlayer player = await World.JoinPlayer("Rider6");
        Entity raft = World.SpawnEntity(Raft, World.Spawn.Offset(3, 1, 0));
        await World.Ticks(2);
        Assert.True(player.Mount(raft));
        BlockPos start = player.Position;

        Exception? thrown = Record.Exception(() => { _ = player.WalkTo(start.Offset(4, 0, 0)); });

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("Dismount", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(start, player.Position);
    }

    [AtlasScenario]
    public async Task Mount_Should_RejectANullEntity()
    {
        ITestPlayer player = await World.JoinPlayer("Rider7");

        Assert.Throws<ArgumentNullException>(() => player.Mount(null!));
        await World.Ticks(1);
    }
}
