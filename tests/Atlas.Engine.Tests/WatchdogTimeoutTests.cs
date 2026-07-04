using Atlas.XUnit.Internal;

namespace Atlas.Engine.Tests;

/// <summary>
/// Proves the wall-clock watchdog fires off-thread, from the thread pool, while the game thread
/// itself is genuinely blocked inside a scenario - the exact hang the watchdog exists to catch.
/// Lives in its own class: the induced hang wedges this class's host, so it must not share one.
/// </summary>
[Trait("Category", "E2E")]
public class WatchdogTimeoutTests
{
    [Fact]
    public async Task Watchdog_Should_FireOffThread_When_GameThreadIsBlocked()
    {
        string baseDir = AppContext.BaseDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();

        Task scenario = host.RunOnGameThreadAsync(async (api, ticks) =>
        {
            Thread.Sleep(8000); // blocks the pump: the exact hang the watchdog exists to catch
            await ticks.WaitTicksAsync(1);
        });

        var ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(
            () => Watchdog.RunAsync(scenario, timeoutMs: 2000, currentTick: () => 0));

        Assert.Contains("2000 ms", ex.Message);

        // Observe the abandoned scenario so its late completion cannot raise noise.
        _ = scenario.ContinueWith(
            static t => _ = t.Exception,
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
