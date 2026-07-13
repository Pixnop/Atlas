using System.Reflection;

namespace Atlas.Engine.Tests;

/// <summary>Proves the game-thread pump notices an ENGINE-initiated shutdown and reports it as
/// a host crash promptly, instead of spinning silently on the stopped server until an outer job
/// timeout (the failure mode that turned an engine-thread crash into a 30-minute CI hang).
/// Lives in its own class: the induced stop kills this class's host.</summary>
/// <remarks>The stop is induced through the engine's own public shutdown entry point,
/// <c>ServerMain.AttemptShutdown(string, int)</c> (identical on 1.20.12, 1.21.7 and 1.22.3),
/// which ends in the same <c>Stop(...)</c> call that <c>ServerThread.Process</c> enqueues after
/// an unhandled exception in a server thread; by the time it returns, the engine has stopped
/// itself exactly as it does after a chunkdbthread crash, without Atlas's stop token being
/// canceled. Called through reflection: referencing a VintagestoryLib type in a test method
/// body would make the CLR resolve it at JIT time, before any host has booted (the
/// <c>PlayingStateTests</c> convention).</remarks>
[Trait("Category", "E2E")]
public class EngineInitiatedStopTests
{
    [Fact]
    public async Task RunOnGameThread_Should_SurfaceServerCrashedException_When_TheEngineStopsItself()
    {
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), AppContext.BaseDirectory);
        await host.StartAsync();

        Task<ServerCrashedException> crashTask = Assert.ThrowsAsync<ServerCrashedException>(() =>
            host.RunOnGameThreadAsync(async (api, ticks) =>
            {
                object server = api.World;
                MethodInfo attemptShutdown = server.GetType().GetMethod(
                    "AttemptShutdown",
                    BindingFlags.Public | BindingFlags.Instance,
                    [typeof(string), typeof(int)])
                    ?? throw new InvalidOperationException(
                        "engine method 'ServerMain.AttemptShutdown(string, int)' not found; "
                        + "cannot induce an engine-initiated stop on this game version.");
                attemptShutdown.Invoke(server, ["Atlas test-induced engine stop", 500]);

                // Park on a tick wait: the stopped server ticks never again, so only the pump's
                // crash path (fault every waiter with the real cause) can complete this.
                await ticks.WaitTicksAsync(600);
            }));

        // Bound the wait so a regression (the pump spinning blind on the stopped server) fails
        // this test in minutes instead of hanging the whole run to the job timeout.
        Task winner = await Task.WhenAny(crashTask, Task.Delay(TimeSpan.FromMinutes(2)));
        Assert.True(
            ReferenceEquals(winner, crashTask),
            "the host did not report the engine-initiated stop within 2 minutes: the pump is blind to engine shutdowns again");

        ServerCrashedException crash = await crashTask;
        var stopped = Assert.IsType<EngineStoppedException>(crash.InnerException);
        Assert.Contains("stopped itself", stopped.Message);
        Assert.Contains("server-main.log", stopped.Message);
        Assert.NotNull(host.CrashException);
    }
}
