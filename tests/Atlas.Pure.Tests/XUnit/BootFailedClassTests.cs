namespace Atlas.Pure.Tests.XUnit;

using Atlas.Api;
using Atlas.XUnit.Internal;

/// <summary>Covers what the registry does once a class's host failed to boot: the failure is
/// recorded the way a crashed class is (the dead-class record), so every later request for the
/// class fails at once with the same failure instead of booting the server again, and the log
/// report of that one boot stays available to every scenario that reports it.</summary>
/// <remarks>The registry's record is process-wide and permanent for the class it names, so each
/// test marks a private class nobody else asks for, never booted: the path under test runs
/// before any host exists. The collection serializes the class with every other test touching the
/// registry's process-wide gate.</remarks>
[Collection("HostRegistry")]
public class BootFailedClassTests
{
    private const string Report = "[Atlas] server log: /scratch/atlas/deadbeef/Logs/server-main.log";

    [Fact]
    public async Task GetOrCreateAsync_Should_FailAtOnceWithTheBootFailure_When_TheClassBootFailedBefore()
    {
        var failure = new AtlasBootDiagnosticsException("Boot diagnostics: 1 entry was logged");
        Assert.True(HostRegistry.MarkBootFailed(typeof(GetOrCreateProbeScenarios), failure, Report));

        ServerCrashedException ex = await Assert.ThrowsAsync<ServerCrashedException>(
            () => HostRegistry.GetOrCreateAsync(typeof(GetOrCreateProbeScenarios)));

        Assert.Contains(nameof(GetOrCreateProbeScenarios), ex.Message, StringComparison.Ordinal);
        Assert.Contains("Boot diagnostics: 1 entry was logged", ex.Message, StringComparison.Ordinal);
        Assert.Contains("did not boot", ex.Message, StringComparison.Ordinal);
        Assert.Same(failure, ex.InnerException);
    }

    [Fact]
    public async Task RecycleAsync_Should_FailAtOnceWithTheBootFailure_When_TheClassBootFailedBefore()
    {
        var failure = new AtlasSetupException("Mod path(s) not found: nowhere");
        Assert.True(HostRegistry.MarkBootFailed(typeof(RecycleProbeScenarios), failure, logReport: null));

        ServerCrashedException ex = await Assert.ThrowsAsync<ServerCrashedException>(
            () => HostRegistry.RecycleAsync(typeof(RecycleProbeScenarios)));

        Assert.Contains("Mod path(s) not found: nowhere", ex.Message, StringComparison.Ordinal);
        Assert.Same(failure, ex.InnerException);
    }

    [Fact]
    public void MarkBootFailed_Should_LeaveTheClassAlive_When_TheClassBootedBefore()
    {
        HostRegistry.RememberBooted(typeof(BootedBeforeProbeScenarios));

        bool marked = HostRegistry.MarkBootFailed(
            typeof(BootedBeforeProbeScenarios), new InvalidOperationException("one-off"), Report);

        Assert.False(marked);
        Assert.Null(HostRegistry.BootFailureLogReport(typeof(BootedBeforeProbeScenarios)));
    }

    [Fact]
    public void BootFailureLogReport_Should_ReturnTheReportOfTheFailedBoot_When_TheClassBootFailed()
    {
        Assert.True(HostRegistry.MarkBootFailed(typeof(ReportProbeScenarios), new InvalidOperationException("boom"), Report));

        Assert.Equal(Report, HostRegistry.BootFailureLogReport(typeof(ReportProbeScenarios)));
    }

    [Fact]
    public void BootFailureLogReport_Should_ReturnNull_When_TheClassIsDeadForAnotherReason()
    {
        HostRegistry.MarkDead(typeof(CrashedProbeScenarios), "simulated crash for report coverage");

        Assert.Null(HostRegistry.BootFailureLogReport(typeof(CrashedProbeScenarios)));
    }

    [Fact]
    public void BootFailureLogReport_Should_ReturnNull_When_TheClassNeverFailed()
    {
        Assert.Null(HostRegistry.BootFailureLogReport(typeof(NeverFailedProbeScenarios)));
    }

    /// <summary>One private class per test: the dead marker is permanent for the class.</summary>
    private sealed class GetOrCreateProbeScenarios
    {
    }

    private sealed class RecycleProbeScenarios
    {
    }

    private sealed class ReportProbeScenarios
    {
    }

    private sealed class BootedBeforeProbeScenarios
    {
    }

    private sealed class CrashedProbeScenarios
    {
    }

    private sealed class NeverFailedProbeScenarios
    {
    }
}
