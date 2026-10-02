using Atlas.Api;
using Atlas.Internal.Hosting;

namespace Atlas.Engine.Tests;

/// <summary>
/// Proves that a game-thread crash surfaces as <see cref="ServerCrashedException"/> carrying
/// the real cause, instead of leaving scenarios parked until the watchdog fires. Lives in its
/// own class: the induced crash kills this class's host.
/// </summary>
[Trait("Category", "E2E")]
public class CrashSurfacingTests
{
    [Fact]
    public async Task RunOnGameThread_Should_SurfaceServerCrashedException_When_PumpDies()
    {
        string dataPath;
        {
            await using ServerHost host = TestHosts.New();
            await host.StartAsync();

            ServerCrashedException crash = await Assert.ThrowsAsync<ServerCrashedException>(() =>
                host.RunOnGameThreadAsync(async (api, ticks) =>
                {
                    // Post a poison callback outside the scenario's own await capture: the pump's
                    // DrainPending executes it directly, which kills the game thread the same way
                    // a fatal engine failure would.
                    SynchronizationContext.Current!.Post(
                        _ => throw new InvalidOperationException("test-induced crash"),
                        null);

                    // Park on a tick wait; the crash path must fail this waiter promptly with the
                    // real cause rather than leaving it pending forever.
                    await ticks.WaitTicksAsync(600);
                }));

            Assert.IsType<InvalidOperationException>(crash.InnerException);
            Assert.Contains("test-induced crash", crash.InnerException!.Message);
            Assert.NotNull(host.WrapCrashIfAny());
            dataPath = host.DataPath;
        }

        // A crashed host's scratch is the post-mortem evidence: the dispose at the end of the
        // block above must not sweep it (issue #182), though nothing here ran through the
        // registry's failure ledger.
        try
        {
            Assert.True(
                File.Exists(Path.Combine(dataPath, "Logs", "server-main.log")),
                "a crashed host must keep its scratch and the server log inside it");
        }
        finally
        {
            Directory.Delete(dataPath, recursive: true);
        }
    }
}
