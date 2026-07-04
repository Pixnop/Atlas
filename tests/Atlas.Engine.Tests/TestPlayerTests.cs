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
    public async Task GiveItem_Should_ResolveDocumentedExampleCode_When_GivenFlint()
    {
        // Regression coverage for the ITestPlayer.GiveItem doc example: "game:flint" must
        // actually resolve, unlike bare variant-group codes such as "game:bread-spelt".
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");

            await player.GiveItem("game:flint", 1);

            var activeSlot = player.Player.InventoryManager.ActiveHotbarSlot;
            Assert.NotNull(activeSlot.Itemstack);
            Assert.Equal("game:flint", activeSlot.Itemstack.Item.Code.ToString());
        });
    }

    [Fact]
    public async Task GiveItem_Should_ThrowArgumentOutOfRangeException_When_QuantityIsZeroOrLess()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => player.GiveItem("game:flint", 0));
        });
    }

    [Fact]
    public async Task GiveItem_Should_ThrowArgumentOutOfRangeException_When_QuantityExceedsMaxStackSize()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");

            // game:flint's maxstacksize is 64 (assets/survival/itemtypes/resource/flint.json).
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => player.GiveItem("game:flint", 65));
        });
    }

    [Fact]
    public async Task GiveItem_Should_UseGiveItemParameterName_When_CodeIsUnknown()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AtlasTestPlayer");

            ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
                () => player.GiveItem("game:not-a-real-item", 1));

            Assert.Equal("itemOrBlockCode", ex.ParamName);
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

    [Fact]
    public async Task JoinPlayer_Should_ThrowActionableAtlasSetupException_When_ServerRejectsTheJoin()
    {
        // The embedded server disconnects a client with an invalid player name (confirmed: names
        // longer than the engine's limit are rejected with "invalid Playername" in the server
        // log) before its entity ever spawns - the same "server rejected the join" shape a real
        // network-version drift would produce. This exercises WaitForJoin's diagnosis without
        // needing an actual version mismatch.
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            AtlasSetupException ex = await Assert.ThrowsAsync<AtlasSetupException>(
                () => world.JoinPlayer("ThisPlayerNameIsFarTooLongToBeAccepted"));

            Assert.Contains("did not finish joining", ex.Message);
            Assert.Contains("network-version drift", ex.Message);
            Assert.Contains("check the server logs", ex.Message);

            // The failed claim must be released: retrying with a valid name must succeed, not
            // fail with an "already joined" guard left over from the rejected attempt.
            ITestPlayer retry = await world.JoinPlayer("AtlasTestPlayer");
            Assert.NotNull(retry.Entity);
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
