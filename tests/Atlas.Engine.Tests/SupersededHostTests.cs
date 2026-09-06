using Atlas.XUnit.Internal;

namespace Atlas.Engine.Tests;

/// <summary>Pins how <see cref="HostRegistry"/> treats a cached class host that another
/// <see cref="ServerHost"/> boot superseded (see <see cref="ServerHost.IsSuperseded"/>): the
/// boot severs the older host's tick feed and its teardown nulls the engine statics the older
/// host still uses, so that host either hangs on its next tick wait (no tick, so no tick-counted
/// timeout either) or crashes on its next engine log line. Atlas's own engine tests boot direct
/// hosts while a registry host is live all the time; the registry must never hand such a host
/// back to its class. This was the root cause of the <c>ScratchSweepTests</c> hand-off flake: its
/// nested run of a guinea pig class reused the host an earlier nested run had left live, after
/// direct-host classes had booted in between, and hung in the rollback capture.</summary>
[Trait("Category", "E2E")]
public class SupersededHostTests
{
    [Fact]
    public async Task GetOrCreate_Should_RebootTheClassHost_When_AnotherHostBootedSinceItWasHandedOut()
    {
        ServerHost first = await HostRegistry.GetOrCreateAsync(typeof(SupersededProbeScenarios));

        await using (ServerHost direct = TestHosts.New())
        {
            await direct.StartAsync();
        }

        ServerHost again = await HostRegistry.GetOrCreateAsync(typeof(SupersededProbeScenarios));
        Assert.NotSame(first, again);

        // Bounded so a regression fails instead of hanging until the job timeout: a superseded
        // host never ticks again.
        await again.RunScenarioAsync(world => world.Ticks(1)).WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>Probe class owning the registry host under test; never runs scenarios.</summary>
    private sealed class SupersededProbeScenarios
    {
    }
}
