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
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
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
    }
}
