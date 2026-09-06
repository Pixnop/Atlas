namespace Atlas.Pure.Tests.XUnit;

using Atlas.Api;
using Atlas.XUnit.Internal;

/// <summary>Covers the dead-host fail-fast path: once a scenario crashed or abandoned a class's
/// host, every later request for that class fails immediately with the recorded message instead
/// of booting a replacement.</summary>
/// <remarks><para>This lived in the engine suite and booted a real server, only because it was an
/// <c>[AtlasScenario]</c> class. It never needed one: <c>ThrowIfDead</c> runs first thing inside
/// <c>GetOrCreateAsync</c>, so no host is ever created on this path.</para>
/// <para>The dead marker is process-wide and permanent for the class it names, which is why the
/// marked class is a private nobody else asks for. The collection serializes this class with
/// every other test touching the registry's process-wide gate.</para></remarks>
[Collection("HostRegistry")]
public class DeadHostFailFastTests
{
    [Fact]
    public async Task GetOrCreateAsync_Should_FailFast_When_TheClassHostWasMarkedDead()
    {
        HostRegistry.MarkDead(typeof(DeadProbeScenarios), "simulated crash for fail-fast coverage");

        ServerCrashedException ex = await Assert.ThrowsAsync<ServerCrashedException>(
            () => HostRegistry.GetOrCreateAsync(typeof(DeadProbeScenarios)));

        Assert.Contains("simulated crash for fail-fast coverage", ex.Message);
    }

    /// <summary>The class the marker names; never booted, so marking it dead costs nothing.</summary>
    private sealed class DeadProbeScenarios
    {
    }
}
