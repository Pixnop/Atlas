using System.Diagnostics;
using Atlas.XUnit.Internal;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Covers the world snapshot/rollback engine component (spec
/// docs/specs/2026-07-06-world-snapshot-rollback.md, stage 1) at the <see cref="ServerHost"/>
/// seam: correctness of a rollback against recorded expectations, lazy snapshot capture, the
/// stage 1 test-player guard, the fail-closed fallback to a full host recycle, and the measured
/// speedup over that recycle. The authoring surface on top of this
/// (<c>[AtlasScenario(RollbackWorld = true)]</c>) is covered by <c>AdapterRollbackTests</c>.</summary>
[Trait("Category", "E2E")]
public class WorldRollbackTests
{
    private const string PatternBlockA = "game:soil-medium-normal";
    private const string PatternBlockB = "game:rock-granite";
    private const string PolluteBlock = "game:rock-andesite";
    private const string ModDataKey = "atlas-rollback-test";

    [Fact]
    public async Task TryRollbackWorld_Should_RestoreSnapshotWorld_And_BeFasterThanHostRecycle()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE the boot redirects it
        var hostA = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        ServerHost? hostB = null;
        bool hostADisposed = false;
        try
        {
            await hostA.StartAsync();

            BlockPos origin = null!;
            BlockPos extraPos = null!;
            long chickenAId = 0;
            long chickenBId = 0;

            // Phase 1: seed the pre-snapshot world: a distinctive 5x5 two-block checkerboard one
            // block above the terrain, one entity, one savegame moddata entry.
            await hostA.RunScenarioAsync(async world =>
            {
                origin = world.Spawn.Offset(2, 1, 2);
                for (int dx = 0; dx < 5; dx++)
                {
                    for (int dz = 0; dz < 5; dz++)
                    {
                        world.SetBlock((dx + dz) % 2 == 0 ? PatternBlockA : PatternBlockB, origin.Offset(dx, 0, dz));
                    }
                }

                Entity chickenA = world.SpawnEntity("game:chicken-rooster", origin.Offset(2, 1, 2));
                chickenAId = chickenA.EntityId;
                world.Api.WorldManager.SaveGame.StoreData(ModDataKey, new byte[] { 1, 2, 3 });
                await world.Ticks(5);
            });

            // Phase 2: the first rollback request captures the snapshot (lazy: nothing was
            // captured at boot) and restores nothing, so the seeded world must survive it.
            Assert.False(hostA.HasWorldSnapshot, "snapshot existed before any rollback request");
            Assert.True(await hostA.TryRollbackWorldAsync(), "first rollback request (capture) failed");
            Assert.True(hostA.HasWorldSnapshot, "first rollback request did not capture a snapshot");

            // Phase 3: record the snapshot-time expectations over a probe box that covers the
            // pattern plus an air margin (so pollution placed above/next to it is checked too).
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
            await hostA.RunScenarioAsync(world =>
            {
                expectedIds = probe.Select(p => world.BlockAt(p).BlockId).ToArray();
                Assert.NotEqual(0, world.BlockAt(origin).BlockId); // the pattern survived the capture
                return Task.CompletedTask;
            });

            // Phase 4: pollute everything the rollback must undo: overwrite and delete pattern
            // blocks, add a block where there was air, spawn a second entity, overwrite the
            // moddata, advance the calendar, and force a brand-new chunk column into existence.
            await hostA.RunScenarioAsync(async world =>
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

                Entity chickenB = world.SpawnEntity("game:chicken-hen", origin.Offset(1, 1, 1));
                chickenBId = chickenB.EntityId;
                world.Api.WorldManager.SaveGame.StoreData(ModDataKey, new byte[] { 9, 9, 9 });
                world.Calendar.Add(6f);

                // A column ~10 chunks away from spawn: untouched at snapshot time, so loading it
                // creates chunks the rollback must discard again.
                int extraChunkX = (origin.X / 32) + 10;
                int extraChunkZ = origin.Z / 32;
                bool loaded = false;
                world.Api.WorldManager.LoadChunkColumnPriority(
                    extraChunkX, extraChunkZ, new ChunkLoadOptions { OnLoaded = () => loaded = true });
                await world.Until(() => loaded, timeoutTicks: 3000);

                var extraBase = new BlockPos((extraChunkX * 32) + 16, 0, (extraChunkZ * 32) + 16, 0);
                extraPos = new BlockPos(
                    extraBase.X,
                    world.Api.World.BlockAccessor.GetTerrainMapheightAt(extraBase) + 1,
                    extraBase.Z,
                    0);
                world.SetBlock(PatternBlockA, extraPos);
                await world.Ticks(2);
            });

            // Phase 5: sanity: the pollution is really live before we roll it back.
            double totalHoursAfterPollution = 0;
            await hostA.RunScenarioAsync(world =>
            {
                Assert.NotEqual(expectedIds, probe.Select(p => world.BlockAt(p).BlockId).ToArray());
                Assert.Contains(
                    world.EntitiesIn(origin.Area(48)),
                    e => e.EntityId == chickenBId);
                Assert.Equal(PatternBlockA, world.BlockAt(extraPos).Code.ToString());
                totalHoursAfterPollution = world.Calendar.TotalHours;
                return Task.CompletedTask;
            });

            // Phase 6: the measured rollback (this time the snapshot exists, so it restores).
            var rollbackWatch = Stopwatch.StartNew();
            Assert.True(await hostA.TryRollbackWorldAsync(), "rollback (restore) failed");
            rollbackWatch.Stop();

            // Phase 7: block-for-block and entity correctness against the recorded expectations.
            await hostA.RunScenarioAsync(world =>
            {
                int[] actualIds = probe.Select(p => world.BlockAt(p).BlockId).ToArray();
                Assert.Equal(expectedIds, actualIds);

                IReadOnlyList<Entity> entities = world.EntitiesIn(origin.Area(48));
                Assert.Contains(entities, e => e.EntityId == chickenAId);
                Assert.DoesNotContain(entities, e => e.EntityId == chickenBId);

                Assert.Equal(new byte[] { 1, 2, 3 }, world.Api.WorldManager.SaveGame.GetData(ModDataKey));
                Assert.True(
                    world.Calendar.TotalHours < totalHoursAfterPollution - 5,
                    $"calendar did not roll back: {world.Calendar.TotalHours} vs {totalHoursAfterPollution}");

                // The extra column the pollution generated is gone (not loaded anymore).
                Assert.Null(world.Api.World.BlockAccessor.GetChunkAtBlockPos(extraPos));
                return Task.CompletedTask;
            });

            // Phase 8: the baseline. A FreshWorld recycle is dispose-old-host + boot-new-host,
            // so time exactly that. No hard multiplier: CI runners are too noisy for one, but a
            // rollback slower than the recycle it replaces would make the feature pointless.
            var recycleWatch = Stopwatch.StartNew();
            await hostA.DisposeAsync();
            hostADisposed = true;
            hostB = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
            await hostB.StartAsync();
            recycleWatch.Stop();
            await hostB.RunScenarioAsync(world =>
            {
                Assert.NotNull(world.BlockAt(world.Spawn).Code); // the recycled host is really alive
                return Task.CompletedTask;
            });

            double speedup = (double)recycleWatch.ElapsedMilliseconds / rollbackWatch.ElapsedMilliseconds;
            Console.Error.WriteLine($"[world-rollback] rollback: {rollbackWatch.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"[world-rollback] host recycle (dispose + boot): {recycleWatch.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"[world-rollback] speedup: {speedup:0.0}x");
            string tooSlow = $"rollback ({rollbackWatch.ElapsedMilliseconds} ms) was not faster " +
                $"than a host recycle ({recycleWatch.ElapsedMilliseconds} ms)";
            Assert.True(rollbackWatch.ElapsedMilliseconds < recycleWatch.ElapsedMilliseconds, tooSlow);
        }
        finally
        {
            if (!hostADisposed)
            {
                await hostA.DisposeAsync();
            }

            if (hostB != null)
            {
                await hostB.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task TryRollbackWorld_Should_ThrowSetupException_When_TestPlayersHaveJoined()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            await world.JoinPlayer("rollback-guard");
        });

        var ex = await Assert.ThrowsAsync<AtlasSetupException>(host.TryRollbackWorldAsync);

        Assert.Contains("test players", ex.Message);
        Assert.Contains("FreshWorld", ex.Message);
        Assert.False(host.HasWorldSnapshot, "the guard must fire before anything is captured");
    }

    [Fact]
    public async Task RollbackOrRecycle_Should_RecycleHostAndWarn_When_SnapshotCaptureFails()
    {
        // Full fail-closed path, exactly as the xUnit invoker drives it: the seam-injected
        // capture failure must degrade RollbackOrRecycleAsync to a fresh host boot (the
        // FreshWorld path), with the one-line warning on stderr, and the scenario must still
        // get a live, clean world.
        ServerHost original = await HostRegistry.GetOrCreateAsync(typeof(FallbackProbeScenarios));
        original.WorldSnapshotFactory =
            (api, ticks) => throw new InvalidOperationException("simulated capture failure");

        var stderr = new StringWriter();
        TextWriter realStderr = Console.Error;
        ServerHost replacement;
        try
        {
            Console.SetError(stderr);
            replacement = await HostRegistry.RollbackOrRecycleAsync(typeof(FallbackProbeScenarios));
        }
        finally
        {
            Console.SetError(realStderr);
        }

        Assert.NotSame(original, replacement);
        Assert.False(replacement.HasWorldSnapshot);
        string warning = stderr.ToString();
        Assert.Contains("[Atlas] world rollback failed", warning);
        Assert.Contains("falling back to a full host recycle", warning);
        Assert.Contains("simulated capture failure", warning);

        await replacement.RunScenarioAsync(world =>
        {
            Assert.NotNull(world.BlockAt(world.Spawn).Code); // the fallback host is really alive
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Snapshot_Should_ExposeDiagnostics_And_GuardMisuse_When_DrivenDirectly()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();

        await host.RunOnGameThreadAsync(async (api, ticks) =>
        {
            var snapshot = Atlas.Internal.Rollback.WorldSnapshot.Create(api, ticks);

            Assert.False(snapshot.IsCaptured);
            Assert.Equal(0, snapshot.SnapshotChunkCount);
            Assert.Empty(snapshot.SnapshotColumns);
            await Assert.ThrowsAsync<InvalidOperationException>(snapshot.RestoreAsync);

            await snapshot.CaptureAsync();

            Assert.True(snapshot.IsCaptured);
            Assert.True(snapshot.SnapshotChunkCount > 0, "capture recorded no chunk blobs");
            Assert.NotEmpty(snapshot.SnapshotColumns);
        });
    }

    [Fact]
    public async Task Capture_Should_FailSetup_When_ATestPlayerIsJoined()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            await world.JoinPlayer("Squatter");
        });

        await host.RunOnGameThreadAsync(async (api, ticks) =>
        {
            var snapshot = Atlas.Internal.Rollback.WorldSnapshot.Create(api, ticks);
            AtlasSetupException error = await Assert.ThrowsAsync<AtlasSetupException>(snapshot.CaptureAsync);
            Assert.Contains("players", error.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Rollback_Should_UndoRowsPersistedAfterCapture_When_AnAutosaveRanMidScenario()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();

        BlockPos marker = null!;
        await host.RunScenarioAsync(async world =>
        {
            marker = world.Spawn.Offset(3, 1, 0);
            world.SetBlock(PatternBlockA, marker);
            await world.Ticks(2);
        });

        Assert.True(await host.TryRollbackWorldAsync(), "capture (first rollback request) failed");

        // Persist post-snapshot state: load a column in a fresh map region, place a block there,
        // overwrite the marker, and force a save. The database now holds post-snapshot rows
        // (chunk, map chunk, map region) that the restore must delete again, exercising all
        // three DeleteExtraRows branches instead of only the in-memory unload path.
        await host.RunScenarioAsync(async world =>
        {
            int farChunkX = (world.Spawn.X / 32) + 24;
            int farChunkZ = (world.Spawn.Z / 32) + 24;
            bool loaded = false;
            world.Api.WorldManager.LoadChunkColumnPriority(
                farChunkX, farChunkZ, new ChunkLoadOptions { OnLoaded = () => loaded = true });
            await world.Until(() => loaded, timeoutTicks: 3000);

            var farBase = new BlockPos((farChunkX * 32) + 16, 0, (farChunkZ * 32) + 16, 0);
            var farPos = new BlockPos(
                farBase.X,
                world.Api.World.BlockAccessor.GetTerrainMapheightAt(farBase) + 1,
                farBase.Z,
                0);
            world.SetBlock(PolluteBlock, farPos);
            world.SetBlock(PolluteBlock, marker);

            CommandResult saved = await world.ExecuteCommand("/autosavenow");
            Assert.True(saved.Ok, saved.Message);
            await world.Ticks(2);
        });

        Assert.True(await host.TryRollbackWorldAsync(), "rollback after a mid-scenario autosave failed");

        await host.RunScenarioAsync(world =>
        {
            Assert.Equal(PatternBlockA, world.BlockAt(marker).Code.ToString());
            return Task.CompletedTask;
        });
    }

    /// <summary>Marker class owning the <see cref="HostRegistry"/> host of the fallback test.
    /// Never runs as a scenario class; it only keys the registry.</summary>
    private sealed class FallbackProbeScenarios
    {
    }
}
