using System.Reflection;
using Vintagestory.API.MathTools;

namespace Atlas.Engine.Tests;

/// <summary>
/// Regression coverage for issue #84: <c>JoinPlayer</c> must not hand the world back to the
/// scenario while the engine's background server-assets build (queued at boot by
/// <c>ServerMain.Launch()</c> on a TyronThreadPool thread) can still be enumerating live game
/// content. A scenario mutating that content mid-enumeration (the field case: a 2048-SetBlock
/// burst right after the first join) made the build throw "Collection was modified" on the pool
/// thread, and an unhandled pool-thread exception kills the whole testhost process. JoinPlayer
/// now waits for the same completion signal the dispose-side guard reads (the
/// <c>serverAssetsPacket</c> box: <c>packet</c> set, or <c>Length</c> &gt; 0), which is what
/// makes the first test deterministic: the signal must already be settled the moment JoinPlayer
/// returns, no timing window needs to be hit. The signal is read reflectively off
/// <c>object</c> on purpose (the <see cref="TeardownDiagnosticsTests"/> pattern): referencing
/// VintagestoryLib types in a test method body would make the CLR resolve them at JIT time,
/// before any host has booted and installed Atlas's AssemblyResolve hook.
/// </summary>
[Trait("Category", "E2E")]
public class JoinAssetsBuildGuardTests
{
    [Fact]
    public async Task JoinPlayer_Should_HaveSettledAssetsBuild_When_Returning()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            await world.JoinPlayer("AssetsGuard");

            Assert.True(
                AssetsBuildSignalSettled(world),
                "the server-assets build completion signal must already be set when JoinPlayer returns");
        });

        Assert.Null(host.CrashException);
    }

    [Fact]
    public async Task JoinPlayer_Should_KeepTesthostAlive_When_ScenarioBurstsSetBlockRightAfterJoin()
    {
        // Stress shaped like the StratumParity field case: the scenario's very next act after
        // its first join is a 2048-block mutation burst on the game thread. With the guard in
        // place the build has settled before the burst can start, so the burst cannot overlap
        // the build's enumeration no matter how slow the build was; without it, this is exactly
        // the shape that killed the testhost twice on a 4-core CI runner.
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();
        await host.RunScenarioAsync(async world =>
        {
            ITestPlayer player = await world.JoinPlayer("AssetsBurst");

            BlockPos origin = world.Spawn;
            for (int i = 0; i < 2048; i++)
            {
                // A 16x16 footprint, 8 layers high, above the terrain at spawn (chunks there are
                // loaded for the joined player); positions vary so every SetBlock is real work.
                world.SetBlock("game:rock-andesite", origin.Offset(i % 16, 1 + (i / 256), (i / 16) % 16));
            }

            // A couple of pumped ticks give any still-running enumeration (the bug this guards
            // against) time to trip over the mutations before the scenario ends.
            await world.Ticks(2);

            Assert.True(player.IsConnected);
            Assert.Equal("rock-andesite", world.BlockAt(origin.Offset(0, 1, 0)).Code.Path);
        });

        Assert.Null(host.CrashException);
    }

    /// <summary>Reads the engine's assets-build completion signal, either branch (non-dedicated
    /// sets <c>packet</c>, dedicated - or a joining client's send path - bumps <c>Length</c>),
    /// exactly as the guard under test does.</summary>
    private static bool AssetsBuildSignalSettled(IWorldSession world)
    {
        object server = world.Api.World;
        FieldInfo boxField = server.GetType().GetField(
            "serverAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "ServerMain.serverAssetsPacket not found; the engine shape drifted.");
        object box = boxField.GetValue(server)!;
        FieldInfo packetField = box.GetType().GetField(
            "packet", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "serverAssetsPacket.packet not found; the engine shape drifted.");
        FieldInfo lengthField = box.GetType().GetField(
            "Length", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "serverAssetsPacket.Length not found; the engine shape drifted.");
        return packetField.GetValue(box) != null || (int)lengthField.GetValue(box)! != 0;
    }
}
