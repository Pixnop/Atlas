using System.Reflection;

namespace Atlas.Engine.Tests;

/// <summary>
/// Proves the teardown diagnostics fire: the swallowed Vintage Story shutdown
/// NullReferenceException (issue #8) is logged with its stack, and an expired game-thread join
/// logs the abandoned-teardown warning. Both tests deliberately dirty their host's teardown, so
/// each builds its own host and nothing is shared.
/// </summary>
[Trait("Category", "E2E")]
public class TeardownDiagnosticsTests
{
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
            await host.RunOnGameThreadAsync((api, _) =>
            {
                object server = api.World;
                FieldInfo assetsField = server.GetType().GetField(
                    "serverAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance)!;
                object box = assetsField.GetValue(server)!;
                FieldInfo packetField = box.GetType().GetField(
                    "packet", BindingFlags.NonPublic | BindingFlags.Instance)!;

                // The non-dedicated build's completion signal (the engine polls the same state
                // while a joining player waits on it).
                for (int i = 0; packetField.GetValue(box) == null; i++)
                {
                    if (i >= 1200)
                    {
                        throw new InvalidOperationException(
                            "server assets packet was not built within 60s; cannot induce the teardown NRE safely.");
                    }

                    Thread.Sleep(50);
                }

                assetsField.SetValue(server, null);
                return Task.CompletedTask;
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

            // Wedge the game thread past the shortened join timeout. Deliberately NOT awaited
            // before disposing: the pump is stuck inside this work, exactly like a wedged
            // server.Process() call, so DisposeAsync's join must give up and warn.
            Task wedge = host.RunOnGameThreadAsync((_, _) =>
            {
                Thread.Sleep(1500);
                return Task.CompletedTask;
            });

            await host.DisposeAsync();

            Assert.Contains("game thread did not exit within", stderr.ToString());
            Assert.Contains("abandoned", stderr.ToString());

            await wedge;
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
