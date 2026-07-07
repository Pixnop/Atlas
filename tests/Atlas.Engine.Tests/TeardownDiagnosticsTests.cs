using System.Reflection;

namespace Atlas.Engine.Tests;

/// <summary>
/// Proves the teardown diagnostics fire (the swallowed Vintage Story shutdown
/// NullReferenceException of issue #8 is logged with its stack, and an expired game-thread join
/// logs the abandoned-teardown warning), and that teardown itself is safe against the boot's
/// background assets build. Every test deliberately exercises its host's teardown, so each
/// builds its own host and nothing is shared.
/// </summary>
[Trait("Category", "E2E")]
public class TeardownDiagnosticsTests
{
    [Fact]
    public async Task DisposeAsync_Should_NotCrashProcess_When_DisposedDuringBootAssetsBuild()
    {
        // Deterministic repro of the process-killing boot race: ServerMain.Launch() queues
        // BuildServerAssetsPacket on the engine's thread pool; that build reads the statics
        // ServerMain.ClassRegistry and ServerMain.Logger, and its only outer catch handles
        // ThreadAbortException. ServerMain.Dispose() nulls both statics. Disposing a host
        // immediately after boot used to land Dispose inside the still-running build (1-3s bare,
        // wider under coverage instrumentation): the build NRE'd on the nulled registry, its
        // catch NRE'd again on the nulled logger, and the unhandled pool-thread exception killed
        // the whole xUnit testhost. Five back-to-back boot/dispose cycles keep landing teardown
        // inside that window; the fix (ServerHost waits for the build's completion signal before
        // letting Dispose run) must keep the process alive through all of them.
        for (int i = 0; i < 5; i++)
        {
            var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
            await host.StartAsync();
            await host.DisposeAsync();

            Assert.Null(host.CrashException);
        }
    }

    [Fact]
    public async Task DisposeAsync_Should_LogSwallowedShutdownNre_When_EngineDisposeThrows()
    {
        var stderr = new StringWriter();
        TextWriter originalStderr = Console.Error;
        Console.SetError(stderr);
        try
        {
            await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
            await host.StartAsync();

            // Induce the issue #8 shutdown NRE at the first engine dereference inside
            // ServerMain.Dispose(): `serverAssetsPacket.Dispose();` is its unguarded first
            // statement (identical in 1.22.0-1.22.3). Nulling the field is safe because its only
            // readers are that line, the client-join path (no client ever joins here) and the
            // boot's background packet build - which the poll below first waits out, since the
            // real trigger (nulling the static ServerMain.Logger, what an overlapping lifecycle's
            // Dispose does) turns any engine background hiccup into an unhandled process crash.
            await host.RunOnGameThreadAsync(async (api, _) =>
            {
                object server = api.World;
                FieldInfo assetsField = server.GetType().GetField(
                    "serverAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)!;
                object box = assetsField.GetValue(server)!;
                FieldInfo packetField = box.GetType().GetField(
                    "packet", BindingFlags.NonPublic | BindingFlags.Instance)!;

                // The build's completion signal, either branch (the engine polls the same state
                // while a joining player waits on it): non-dedicated sets `packet`, dedicated
                // serializes into the box and bumps `Length`.
                FieldInfo lengthField = box.GetType().GetField(
                    "Length", BindingFlags.Public | BindingFlags.Instance)!;
                bool Built() => packetField.GetValue(box) != null || (int)lengthField.GetValue(box)! != 0;
                for (int i = 0; !Built(); i++)
                {
                    if (i >= 1200)
                    {
                        throw new InvalidOperationException(
                            "server assets packet was not built within 60s; cannot induce the teardown NRE safely.");
                    }

                    // Awaited, not slept: the continuation posts back through the game-thread
                    // scheduler, so the pump keeps processing while the poll waits.
                    await Task.Delay(50);
                }

                assetsField.SetValue(server, null);
            });
        }
        finally
        {
            Console.SetError(originalStderr);
        }

        Assert.Contains("shutdown NRE (issue #8)", stderr.ToString());
        Assert.Contains("NullReferenceException", stderr.ToString());
    }

    [Fact]
    public async Task DisposeAsync_Should_WarnAboutAbandonedGameThread_When_JoinTimesOut()
    {
        var stderr = new StringWriter();
        TextWriter originalStderr = Console.Error;
        Console.SetError(stderr);
        ServerHost? host = null;
        try
        {
            host = new ServerHost(
                new WorldOptions(),
                Array.Empty<string>(),
                AppContext.BaseDirectory,
                gameThreadJoinTimeout: TimeSpan.FromMilliseconds(100));
            await host.StartAsync();

            // No wedge needed: even a healthy teardown outlives the shortened join, because the
            // engine's Stop() polls its own server threads in 500ms steps (its first liveness
            // check alone sits past this timeout). Scheduling wedge work instead would race the
            // pump: cancellation can win before the work is ever drained, and a task that never
            // ran never completes.
            await host.DisposeAsync();

            Assert.Contains("game thread did not exit within", stderr.ToString());
            Assert.Contains("abandoned", stderr.ToString());
        }
        finally
        {
            Console.SetError(originalStderr);

            // The join gave up by design, so wait out the real teardown here: the abandoned
            // thread's late ServerMain.Dispose() nulls process-wide engine statics, and letting
            // it land under the next test's host would recreate the very issue #8 hazard the
            // warning exists to flag.
            if (host?.GameThread is { } thread)
            {
                await Task.Run(() => thread.Join(TimeSpan.FromSeconds(60)));
            }
        }
    }
}
