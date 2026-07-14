using Atlas.XUnit.Internal;

namespace Atlas.Engine.Tests;

/// <summary>Covers the RestartWorld guard rails and observability at the
/// <see cref="HostRegistry"/> seam (the adapter path itself is covered by
/// <see cref="AdapterRestartTests"/>): the players-joined guard fails the request without
/// touching the class host, a completed restart replaces the host instance, cleans up the
/// harvested hand-off save, reports its measured cost, and shows up as a restart count with
/// the accumulated cost in the per-class isolation summary when the class hands its host
/// off.</summary>
[Trait("Category", "E2E")]
public class RestartIsolationTests
{
    [Fact]
    public async Task RestartAsync_Should_FailWithSetupError_When_TestPlayersAreJoined()
    {
        ServerHost host = await HostRegistry.GetOrCreateAsync(typeof(PlayersJoinedProbeScenarios));
        await host.RunScenarioAsync(async session => await session.JoinPlayer("RestartGuardPlr"));

        var ex = await Assert.ThrowsAsync<AtlasSetupException>(
            () => HostRegistry.RestartAsync(typeof(PlayersJoinedProbeScenarios)));

        Assert.Contains("joined test players", ex.Message);
        Assert.Contains("would not survive the restart", ex.Message);

        // The guard fired BEFORE anything was shut down: the class host survives untouched, so
        // later scenarios of the class (players included) keep running.
        Assert.Same(host, await HostRegistry.GetOrCreateAsync(typeof(PlayersJoinedProbeScenarios)));
    }

    [Fact]
    public async Task RestartAsync_Should_ReplaceHostCleanUpHarvestAndCountInSummary_When_RestartCompletes()
    {
        ServerHost original = await HostRegistry.GetOrCreateAsync(typeof(SummaryProbeScenarios));
        string harvestedSavePath = Path.Combine(
            original.DataPath, "Saves", Atlas.Internal.Staging.DataSeeder.WorldSaveFileName);

        RestartOutcome outcome = await HostRegistry.RestartAsync(typeof(SummaryProbeScenarios));

        // The host instance genuinely changed (a real reboot, not a rollback in place), the
        // harvested save was temporary hand-off state (deleted once the replacement booted),
        // and the measured cost covers a real shutdown + boot, so it cannot be trivially small.
        Assert.NotSame(original, outcome.Host);
        Assert.False(File.Exists(harvestedSavePath), "the harvested save was not cleaned up");

        // Issue #83: with the harvest done and the replacement up, the outgoing host's whole
        // scratch is swept under the same green-so-far rule as any other disposed host, so a
        // RestartWorld-heavy class does not pile up one scratch directory per restart.
        Assert.False(
            Directory.Exists(original.DataPath),
            "the outgoing host's scratch directory was not swept after a green restart");
        Assert.True(
            outcome.Cost > TimeSpan.FromMilliseconds(100),
            $"a real restart cannot cost {outcome.Cost.TotalMilliseconds} ms");

        // The fixture-harvest hand-off is an end-of-class moment: the summary prints to stderr
        // and now counts the restart.
        var stderr = new StringWriter();
        TextWriter realStderr = Console.Error;
        try
        {
            Console.SetError(stderr);
            _ = await HostRegistry.ShutDownAndHarvestSavePathAsync();
        }
        finally
        {
            Console.SetError(realStderr);
        }

        string summary = stderr.ToString();
        Assert.Contains($"[Atlas] isolation summary for {typeof(SummaryProbeScenarios).FullName}", summary);
        Assert.Contains("1 restart(s) (", summary);
        Assert.Contains("s total)", summary);
        Assert.Contains("0 rollback(s) succeeded", summary);
    }

    /// <summary>Probe class whose host the players-joined guard test drives directly.</summary>
    private sealed class PlayersJoinedProbeScenarios
    {
    }

    /// <summary>Probe class whose restart the summary test counts.</summary>
    private sealed class SummaryProbeScenarios
    {
    }
}
