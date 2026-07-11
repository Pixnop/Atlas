using Atlas.Internal.Rollback;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Covers the mini-dimension half of rollback stage 3 (spec
/// docs/specs/2026-07-11-rollback-stage3-mod-cooperation.md, question 3): world state parked in
/// mini-dimension chunk columns (the Manifold EntityTransit/BlockTransit shape) must round-trip
/// through the snapshot exactly as dimension-0 columns do. The fixture mod pregenerates a
/// mini-dimension at BOOT, so every capture here also proves the issue #48 acceptance bar:
/// boot-time mini-dimensions no longer disqualify rollback (stage 1 degraded every rollback of
/// such a class to a full recycle). A dimension created AFTER the capture must vanish on
/// rollback, database rows included; and the snapshot diagnostics expose per-dimension
/// columns.</summary>
[Trait("Category", "E2E")]
public class MiniDimensionRollbackTests
{
    private const string FixtureModDll = "RollbackHookFixtureMod.dll";
    private const string PatternBlockA = "game:soil-medium-normal";
    private const string PatternBlockB = "game:rock-granite";
    private const string PolluteBlock = "game:rock-andesite";

    /// <summary>The mini-dimension the scenario creates through the fixture mod's engine-api
    /// command (the Manifold shape: ids from 10 up).</summary>
    private const int ScenarioDimension = 10;

    /// <summary>The mini-dimension the fixture mod pregenerates at boot (see
    /// RollbackHookFixtureModSystem.PregenDimension), with a granite marker at a deterministic
    /// offset inside the spawn chunk.</summary>
    private const int BootDimension = 9;

    private static string OutputDirectory
        => Path.GetDirectoryName(typeof(MiniDimensionRollbackTests).Assembly.Location)!;

    [Fact]
    public async Task Rollback_Should_RestoreMiniDimensionChunks_When_ScenarioPollutesThem()
    {
        await using var host = new ServerHost(
            new WorldOptions(), new[] { FixtureModDll }, OutputDirectory);
        await host.StartAsync();
        await WaitForPregenAsync(host);

        BlockPos origin = null!;
        BlockPos bootMarker = null!;
        BlockPos bootPollution = null!;
        long chickenAId = 0;
        long chickenBId = 0;

        // Phase 1: park world state in mini-dimension chunk columns, pre-capture: the fixture
        // mod creates the columns through the engine api, the scenario lays a two-block floor
        // pattern and a chunk-stored entity on top of it (EntityTransit/BlockTransit shape).
        await host.RunScenarioAsync(async world =>
        {
            CommandResult created = await world.ExecuteCommand($"/rollbackfx dim-create {ScenarioDimension}");
            Assert.True(created.Ok, created.Message);

            int chunkX = world.Spawn.X / 32;
            int chunkZ = world.Spawn.Z / 32;
            origin = new BlockPos((chunkX * 32) + 10, 4, (chunkZ * 32) + 10, ScenarioDimension);
            bootMarker = new BlockPos((chunkX * 32) + 8, 4, (chunkZ * 32) + 8, BootDimension);
            bootPollution = bootMarker.Offset(0, 2, 0);

            for (int dx = 0; dx < 5; dx++)
            {
                for (int dz = 0; dz < 5; dz++)
                {
                    world.SetBlock((dx + dz) % 2 == 0 ? PatternBlockA : PatternBlockB, origin.Offset(dx, 0, dz));
                }
            }

            Entity chickenA = world.SpawnEntity("game:chicken-rooster", origin.Offset(2, 1, 2));
            chickenAId = chickenA.EntityId;
            Assert.Equal(ScenarioDimension, chickenA.Pos.Dimension);

            // The boot-pregenerated marker is really there before the capture.
            Assert.Equal(PatternBlockB, world.BlockAt(bootMarker).Code.ToString());
            await world.Ticks(5);
        });

        // Phase 2: the capture, with mini-dimension chunks loaded in TWO dimensions (the boot
        // pregen and the scenario's). Stage 1 degraded here ("loaded chunk in dimension ...");
        // the issue #48 acceptance bar is that stage 3 does not.
        RollbackAttempt capture = await host.TryRollbackWorldAsync();
        Assert.True(capture.Succeeded, $"capture degraded with mini-dimensions loaded: {capture.DegradeDetail}");

        // Phase 3: record snapshot-time expectations over a probe box covering the pattern plus
        // an air margin, in the scenario dimension.
        List<BlockPos> probe = new();
        for (int dx = -1; dx <= 5; dx++)
        {
            for (int dz = -1; dz <= 5; dz++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    probe.Add(origin.Offset(dx, dy, dz));
                }
            }
        }

        int[] expectedIds = null!;
        await host.RunScenarioAsync(world =>
        {
            expectedIds = probe.Select(p => world.BlockAt(p).BlockId).ToArray();
            Assert.NotEqual(0, world.BlockAt(origin).BlockId); // the pattern survived the capture
            return Task.CompletedTask;
        });

        // Phase 4: pollute the mini-dimensions: overwrite and delete pattern blocks, add a block
        // where there was air, spawn a second chunk-stored entity, and deface the
        // boot-pregenerated dimension too.
        await host.RunScenarioAsync(async world =>
        {
            for (int dx = 0; dx < 5; dx++)
            {
                for (int dz = 0; dz < 5; dz++)
                {
                    world.SetBlock(PolluteBlock, origin.Offset(dx, 0, dz));
                }
            }

            world.SetBlock("game:air", origin.Offset(1, 0, 1));
            world.SetBlock(PolluteBlock, origin.Offset(1, 2, 1));
            world.SetBlock(PolluteBlock, bootPollution);

            Entity chickenB = world.SpawnEntity("game:chicken-hen", origin.Offset(1, 1, 1));
            chickenBId = chickenB.EntityId;
            await world.Ticks(2);
        });

        // Phase 5: sanity: the pollution is really live before rolling it back.
        await host.RunScenarioAsync(world =>
        {
            Assert.NotEqual(expectedIds, probe.Select(p => world.BlockAt(p).BlockId).ToArray());
            Assert.Contains(world.EntitiesIn(origin.Area(48)), e => e.EntityId == chickenBId);
            Assert.Equal(PolluteBlock, world.BlockAt(bootPollution).Code.ToString());
            return Task.CompletedTask;
        });

        // Phase 6: the rollback under test.
        RollbackAttempt restore = await host.TryRollbackWorldAsync();
        Assert.True(restore.Succeeded, $"rollback degraded: {restore.DegradeDetail}");

        // Phase 7: block-for-block and entity correctness inside the mini-dimensions.
        await host.RunScenarioAsync(world =>
        {
            Assert.Equal(expectedIds, probe.Select(p => world.BlockAt(p).BlockId).ToArray());

            IReadOnlyList<Entity> entities = world.EntitiesIn(origin.Area(48));
            Assert.Contains(entities, e => e.EntityId == chickenAId);
            Assert.DoesNotContain(entities, e => e.EntityId == chickenBId);

            Assert.Equal(PatternBlockB, world.BlockAt(bootMarker).Code.ToString());
            Assert.Equal("game:air", world.BlockAt(bootPollution).Code.ToString());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Rollback_Should_DiscardScenarioCreatedDimension_When_ItWasCreatedAfterTheCapture()
    {
        // No fixture mod here: the engine api is enough to create the post-capture dimension,
        // and this also covers rollback hosts with no mini-dimensions at capture time.
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), OutputDirectory);
        await host.StartAsync();

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");

        BlockPos parked = null!;
        int chunkX = 0;
        int chunkZ = 0;
        await host.RunScenarioAsync(async world =>
        {
            chunkX = world.Spawn.X / 32;
            chunkZ = world.Spawn.Z / 32;
            world.Api.WorldManager.CreateChunkColumnForDimension(chunkX, chunkZ, 11);
            parked = new BlockPos((chunkX * 32) + 7, 4, (chunkZ * 32) + 7, 11);
            world.SetBlock(PatternBlockB, parked);

            // Force a save so the post-capture dimension's rows reach the database: the restore
            // must delete them again (DeleteExtraRows over dimension-keyed positions).
            CommandResult saved = await world.ExecuteCommand("/autosavenow");
            Assert.True(saved.Ok, saved.Message);
            await world.Ticks(2);
        });

        // The restore, with stderr captured to also pin the stage 3 restore-cost
        // instrumentation line (measured duration and the dirty-column ratio).
        var stderr = new StringWriter();
        TextWriter realStderr = Console.Error;
        RollbackAttempt restore;
        try
        {
            Console.SetError(stderr);
            restore = await host.TryRollbackWorldAsync();
        }
        finally
        {
            Console.SetError(realStderr);
        }

        Assert.True(restore.Succeeded, $"rollback degraded: {restore.DegradeDetail}");
        Assert.Contains("[Atlas] world rollback restore #1", stderr.ToString());
        Assert.Contains("dirty columns at restore:", stderr.ToString());

        await host.RunScenarioAsync(async world =>
        {
            // The scenario-created dimension is gone from memory...
            Assert.Null(world.Api.World.BlockAccessor.GetChunkAtBlockPos(parked));

            // ... and from the database: an explicit dimension-aware reload request finds no
            // rows to load (the engine discards a column whose rows are missing), so the chunk
            // stays unloaded even after generous pumping.
            world.Api.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, 11);
            await world.Ticks(100);
            Assert.Null(world.Api.World.BlockAccessor.GetChunkAtBlockPos(parked));
        });
    }

    [Fact]
    public async Task Snapshot_Should_RecordPerDimensionColumns_When_MiniDimensionsAreLoaded()
    {
        await using var host = new ServerHost(
            new WorldOptions(), new[] { FixtureModDll }, OutputDirectory);
        await host.StartAsync();
        await WaitForPregenAsync(host);

        await host.RunOnGameThreadAsync(async (api, ticks) =>
        {
            var snapshot = WorldSnapshot.Create(api, ticks);
            await snapshot.CaptureAsync();

            Assert.Contains(snapshot.SnapshotColumns, c => c.Dimension == 0);
            Assert.Contains(snapshot.SnapshotColumns, c => c.Dimension == BootDimension);
        });
    }

    /// <summary>Waits until the fixture mod finished its boot-time mini-dimension
    /// pregeneration (it defers creation by a few ticks until the spawn map chunk exists).</summary>
    private static Task WaitForPregenAsync(ServerHost host)
        => host.RunScenarioAsync(async world =>
        {
            for (int i = 0; i < 200; i++)
            {
                CommandResult state = await world.ExecuteCommand("/rollbackfx state");
                Assert.True(state.Ok, state.Message);
                if (state.Message.Contains("pregen:true", StringComparison.Ordinal))
                {
                    return;
                }

                await world.Ticks(10);
            }

            Assert.Fail("the fixture mod's boot-time mini-dimension pregeneration never completed");
        });
}
