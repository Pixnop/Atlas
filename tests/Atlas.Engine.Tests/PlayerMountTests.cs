using Atlas.Internal.Bootstrap;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>Pins <c>ITestPlayer.Mount</c> and <c>Dismount</c> against a vanilla raft, which has
/// two seats, and a vanilla rooster, which has none: the player takes the first free seat the
/// entity's <c>IMountable</c> lists, a full entity answers <see langword="false"/> rather than
/// throwing, and both calls act in the same pass.</summary>
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
