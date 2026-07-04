using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

[Trait("Category", "E2E")]
public class TestPlayerTests
{
    [Fact]
    public async Task JoinPlayer_Should_SpawnPlayerPresentInWorld_When_Joined()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE the boot redirects it
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");

            Assert.NotNull(player.Entity);
            IReadOnlyList<Entity> nearby = world.EntitiesIn(player.Position.Area(16));
            Assert.Contains(nearby, e => e.EntityId == player.Entity.EntityId);

            BlockPos spawn = world.Spawn;
            double dx = player.Position.X - spawn.X;
            double dz = player.Position.Z - spawn.Z;
            double distance = Math.Sqrt((dx * dx) + (dz * dz));
            Assert.True(distance < 1024, $"Expected the joined player near spawn, was {distance} blocks away.");
        });
    }

    [Fact]
    public async Task JoinPlayer_Should_ExposeReadableStats_When_Joined()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");

            Assert.True(player.Stats.Health > 0, $"Expected Health > 0, was {player.Stats.Health}");
            Assert.True(player.Stats.MaxHealth > 0, $"Expected MaxHealth > 0, was {player.Stats.MaxHealth}");
        });
    }

    [Fact]
    public async Task GiveItem_Should_PlaceStackInActiveHotbarSlot_When_Given()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");

            await player.GiveItem("game:bread-spelt-perfect", 3);

            var activeSlot = player.Player.InventoryManager.ActiveHotbarSlot;
            Assert.NotNull(activeSlot.Itemstack);
            Assert.Equal("game:bread-spelt-perfect", activeSlot.Itemstack.Item.Code.ToString());
            Assert.Equal(3, activeSlot.Itemstack.StackSize);
        });
    }

    [Fact]
    public async Task TeleportTo_Should_MovePlayerToPosition_When_NearbyPositionGiven()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");
            BlockPos destination = player.Position.Offset(5, 0, 5);

            await player.TeleportTo(destination);
            await world.Until(() => IsNear(player.Position, destination), timeoutTicks: 200);

            Assert.True(IsNear(player.Position, destination), $"Expected player near {destination}, was at {player.Position}.");
        });
    }

    [Fact]
    public async Task StatsOf_Should_ReadHealthOfSpawnedNonPlayerEntity_When_Queried()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            BlockPos pos = world.Spawn.Offset(3, 1, 0);
            Entity creature = world.SpawnEntity("game:chicken-rooster", pos);
            await world.Ticks(5);

            IEntityStats stats = world.StatsOf(creature);

            Assert.True(stats.Health > 0, $"Expected Health > 0, was {stats.Health}");
        });
    }

    [Fact]
    public async Task JoinPlayer_Should_ThrowAtlasSetupException_When_CalledTwice()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            await world.JoinPlayer("FirstPlayer");

            await Assert.ThrowsAsync<AtlasSetupException>(() => world.JoinPlayer("SecondPlayer"));
        });
    }

    private static bool IsNear(BlockPos a, BlockPos b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        int dz = a.Z - b.Z;
        return (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz)) <= 1;
    }
}
