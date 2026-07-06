using System.Diagnostics;
using System.Linq;
using Atlas.Internal.Rollback;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Stage 0 spike for world snapshot/rollback (spec
/// docs/specs/2026-07-06-world-snapshot-rollback.md): measures whether recycling chunk columns
/// through the engine's own persistence round trip restores the world correctly, and how its cost
/// compares to the full host recycle that FreshWorld pays today.</summary>
[Trait("Category", "E2E")]
public class WorldRollbackSpikeTests
{
    private const string PatternBlockA = "game:soil-medium-normal";
    private const string PatternBlockB = "game:rock-granite";
    private const string PolluteBlock = "game:rock-andesite";
    private const string ModDataKey = "atlas-rollback-spike";

    [Fact]
    public async Task Rollback_Should_RestoreSnapshotWorld_AtLeast5xFasterThanHostRecycle()
    {
        string baseDir = AppContext.BaseDirectory; // capture BEFORE the boot redirects it
        var hostA = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        ServerHost? hostB = null;
        bool hostADisposed = false;
        try
        {
            await hostA.StartAsync();

            WorldRollbackSpike spike = null!;
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

            // Phase 2: snapshot (forces one save, waits for it to settle, reads all blobs back).
            await hostA.RunOnGameThreadAsync(async (api, ticks) =>
            {
                spike = WorldRollbackSpike.Create(api, ticks);
                await spike.CaptureAsync();
            });
            Assert.True(spike.SnapshotChunkCount > 0, "snapshot captured no chunk blobs");
            Assert.True(spike.SnapshotColumns.Count > 0, "snapshot recorded no loaded columns");

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
                Assert.NotEqual(0, world.BlockAt(origin).BlockId); // the pattern survived the save
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

            // Phase 6: the measured rollback.
            var rollbackWatch = Stopwatch.StartNew();
            await hostA.RunOnGameThreadAsync((api, ticks) => spike.RollbackAsync());
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
            // so time exactly that.
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
            Console.Error.WriteLine($"[rollback-spike] rollback: {rollbackWatch.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"[rollback-spike] host recycle (dispose + boot): {recycleWatch.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"[rollback-spike] speedup: {speedup:0.0}x (verdict gate: >= 5x)");
            string verdict =
                $"verdict gate failed: rollback {rollbackWatch.ElapsedMilliseconds} ms is only {speedup:0.0}x " +
                $"faster than a host recycle at {recycleWatch.ElapsedMilliseconds} ms (gate: 5x)";
            Assert.True(speedup >= 5, verdict);
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
}
