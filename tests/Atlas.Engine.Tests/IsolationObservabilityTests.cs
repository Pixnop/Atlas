using System.Diagnostics.CodeAnalysis;
using Atlas.Internal.Rollback;
using Atlas.XUnit;
using Atlas.XUnit.Internal;
using Xunit.Abstractions;

namespace Atlas.Engine.Tests;

/// <summary>Covers issue #53 end-to-end: a degraded rollback must be visible in the standard
/// workflow (attached to the test's own output, which travels inside the TestPassed/TestFailed
/// message into TRX, the IDE test explorer and `atlas run`), strict isolation must fail with the
/// degrade reason instead of silently recycling, a genuine crash must never be re-labelled, and
/// the registry must print a per-class isolation summary when a class hands its host off. Each
/// test drives a real <c>AtlasTestCase</c> through the full pipeline (case runner, test runner,
/// invoker, registry, host) against a private probe scenario class, spying on the xUnit message
/// bus: a test cannot assert its own output, so the probe's run is nested (see
/// <see cref="ProbeCases"/>), exactly like <see cref="NestedRunnerTests"/> but per test case.
/// The induction seam is the documented one: a swapped <c>WorldSnapshotFactory</c> on the probe
/// class's live host.</summary>
[Trait("Category", "E2E")]
public class IsolationObservabilityTests
{
    /// <summary>Why the probe bodies below carry no assertion of their own.</summary>
    private const string ProbeJustification =
        "Probe scenario driven by one of the tests above, which asserts the outcome externally, " +
        "on the messages the pipeline queued. The body must stay assertion-free so a regression " +
        "surfaces in the outer test instead of being masked by a different failure.";

    [Fact]
    public async Task DegradedRollback_Should_AttachReasonAndCostToTestOutput_When_ScenarioStillPasses()
    {
        ServerHost original = await HostRegistry.GetOrCreateAsync(typeof(DegradeOutputProbeScenarios));
        original.WorldSnapshotFactory =
            (_, _) => throw new InvalidOperationException("simulated capture failure");

        IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
            typeof(DegradeOutputProbeScenarios),
            nameof(DegradeOutputProbeScenarios.Scenario_Should_StillPass),
            strictIsolation: false);

        // The scenario itself passed (the fallback still delivered a clean world), and the
        // degrade evidence rides in the test's own output: reason, fallback cost, detail.
        ITestPassed passed = Assert.Single(messages.OfType<ITestPassed>());
        Assert.Contains("[Atlas] world isolation degraded", passed.Output);
        Assert.Contains("RollbackWorld fell back to a full host recycle", passed.Output);
        Assert.Contains("cost", passed.Output);
        Assert.Contains("Reason: capture or restore failed.", passed.Output);
        Assert.Contains("InvalidOperationException: simulated capture failure", passed.Output);

        // The same report is also streamed as a live TestOutput message for runners that
        // surface output as it happens.
        Assert.Contains(
            messages.OfType<ITestOutput>(),
            output => output.Output.Contains("[Atlas] world isolation degraded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompletedRestart_Should_AttachCostToTestOutput_When_ScenarioPasses()
    {
        IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
            typeof(RestartOutputProbeScenarios),
            nameof(RestartOutputProbeScenarios.Scenario_Should_StillPass),
            strictIsolation: false,
            restartWorld: true);

        // The scenario passed on the restarted host, and the restart's cost (paid before the
        // timed body, so invisible in the PASS line's own duration) rides in the test's output.
        ITestPassed passed = Assert.Single(messages.OfType<ITestPassed>());
        Assert.Contains("[Atlas] world restarted", passed.Output);
        Assert.Contains("cost", passed.Output);
        Assert.Contains("paid outside the scenario's reported duration", passed.Output);

        // The same report is also streamed as a live TestOutput message.
        Assert.Contains(
            messages.OfType<ITestOutput>(),
            output => output.Output.Contains("[Atlas] world restarted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HealthyRollback_Should_StayQuiet_When_SnapshotWorks()
    {
        IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
            typeof(QuietProbeScenarios),
            nameof(QuietProbeScenarios.Scenario_Should_PassSilently),
            strictIsolation: false);

        ITestPassed passed = Assert.Single(messages.OfType<ITestPassed>());
        Assert.Equal(string.Empty, passed.Output);
        Assert.DoesNotContain(messages, message => message is ITestOutput);
    }

    [Fact]
    public async Task StrictIsolation_Should_FailWithDegradeReason_When_RollbackDegrades()
    {
        ServerHost original = await HostRegistry.GetOrCreateAsync(typeof(StrictProbeScenarios));
        original.WorldSnapshotFactory =
            (_, _) => throw new InvalidOperationException("simulated capture failure");

        IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
            typeof(StrictProbeScenarios),
            nameof(StrictProbeScenarios.Scenario_Should_NotRun),
            strictIsolation: true);

        ITestFailed failed = Assert.Single(messages.OfType<ITestFailed>());
        Assert.Equal("Atlas.Api.AtlasIsolationException", Assert.Single(failed.ExceptionTypes));
        string message = Assert.Single(failed.Messages);
        Assert.Contains("StrictIsolation", message);
        Assert.Contains("Reason: capture or restore failed.", message);
        Assert.Contains("InvalidOperationException: simulated capture failure", message);
        Assert.False(StrictProbeScenarios.BodyRan, "the scenario body ran despite the strict failure");

        // The degrade report is still attached to the failed test's output too.
        Assert.Contains("[Atlas] world isolation degraded", failed.Output);

        // Strictness changes visibility, not safety: the registry already recycled the host, so
        // later scenarios of the class run on a clean world.
        ServerHost replacement = await HostRegistry.GetOrCreateAsync(typeof(StrictProbeScenarios));
        Assert.NotSame(original, replacement);
    }

    [Fact]
    public async Task StrictIsolation_Should_SurfaceCrash_When_RollbackFailureIsACrash()
    {
        ServerHost host = await HostRegistry.GetOrCreateAsync(typeof(CrashProbeScenarios));
        host.WorldSnapshotFactory = (_, _) => throw new ServerCrashedException(
            "simulated crash during rollback", new InvalidOperationException("root cause"));

        try
        {
            IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
                typeof(CrashProbeScenarios),
                nameof(CrashProbeScenarios.Scenario_Should_NotRun),
                strictIsolation: true);

            // A genuine crash is never re-labelled as a strictness failure: it surfaces as the
            // crash it is, exactly as on the non-strict path. ExceptionTypes flattens the inner
            // exception chain, so the outer type is the first entry.
            ITestFailed failed = Assert.Single(messages.OfType<ITestFailed>());
            Assert.Equal("Atlas.Api.ServerCrashedException", failed.ExceptionTypes[0]);
            Assert.DoesNotContain("Atlas.Api.AtlasIsolationException", failed.ExceptionTypes);
            Assert.Contains("simulated crash during rollback", failed.Messages[0]);
        }
        finally
        {
            // The sabotaged factory never degraded (the throw was a crash), so the host was not
            // replaced; recycle it so later tests never meet the poisoned seam.
            await HostRegistry.RecycleAsync(typeof(CrashProbeScenarios));
        }
    }

    [Fact]
    public async Task HostRegistry_Should_PrintIsolationSummary_When_ClassHandsOffItsHost()
    {
        // Build a mixed history for the probe class: one degrade (sabotaged capture), then a
        // successful capture and a successful restore on the replacement host.
        ServerHost sabotaged = await HostRegistry.GetOrCreateAsync(typeof(SummaryProbeScenarios));
        sabotaged.WorldSnapshotFactory =
            (_, _) => throw new InvalidOperationException("simulated capture failure");
        RollbackOutcome degraded = await HostRegistry.RollbackOrRecycleAsync(typeof(SummaryProbeScenarios));
        Assert.True(degraded.Degraded, "the sabotaged rollback did not degrade");
        Assert.True((await HostRegistry.RollbackOrRecycleAsync(typeof(SummaryProbeScenarios))).Degraded is false);
        Assert.True((await HostRegistry.RollbackOrRecycleAsync(typeof(SummaryProbeScenarios))).Degraded is false);

        // The hand-off to another class is the end-of-class moment: the summary line prints.
        string summary = await Stderr.CaptureAsync(async () =>
        {
            _ = await HostRegistry.GetOrCreateAsync(typeof(SummaryHandoffScenarios));
        });

        // The capture (the first successful request after the degrade) is its own line item,
        // so only the genuine restore counts as a rollback succeeded (issue #71).
        Assert.Contains($"[Atlas] isolation summary for {typeof(SummaryProbeScenarios).FullName}", summary);
        Assert.Contains("1 capture (", summary);
        Assert.Contains("1 rollback(s) succeeded (", summary);
        Assert.Contains("1 degraded to a full host recycle (capture or restore failed x1;", summary);
        Assert.Contains("s total)", summary);
        Assert.Contains("0 FreshWorld recycle(s)", summary);
    }

    [Fact]
    public async Task HostRegistry_Should_PrintIsolationSummaryWithRecycleCost_When_ClassOnlyUsedFreshWorld()
    {
        // The issue #71 gap: a FreshWorld-only class pays a full recycle per scenario and used
        // to hand its host off without any summary. Run one FreshWorld scenario through the
        // real pipeline (the invoker records the recycle with the cost measured in
        // HostRegistry.RecycleAsync), then trigger the end-of-class hand-off.
        IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
            typeof(FreshWorldOnlyProbeScenarios),
            nameof(FreshWorldOnlyProbeScenarios.Scenario_Should_Pass),
            strictIsolation: false,
            freshWorld: true);
        Assert.Single(messages.OfType<ITestPassed>());

        string summary = await Stderr.CaptureAsync(async () =>
        {
            _ = await HostRegistry.ShutDownAndHarvestSavePathAsync();
        });
        Assert.Contains($"[Atlas] isolation summary for {typeof(FreshWorldOnlyProbeScenarios).FullName}", summary);
        Assert.Contains("1 FreshWorld recycle(s) (", summary);
        Assert.Contains("s total)", summary);
        Assert.Contains("0 captures", summary);
        Assert.Contains("0 rollback(s) succeeded", summary);
    }

    // The probes are private on purpose (xUnit only discovers public classes, so the outer test
    // run never executes them directly), which trips xUnit1000 on their [AtlasScenario] methods;
    // the attribute must stay because XunitTestCase.Initialize reads the FactAttribute off the
    // method even for manually built cases (the isolation flags themselves are passed to the
    // synthetic AtlasTestCase, mirroring what the discoverer reads from the attribute).
#pragma warning disable xUnit1000

    /// <summary>Probe for the degrade-visibility test.</summary>
    private sealed class DegradeOutputProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(RollbackWorld = true)]
        [SuppressMessage(
            "Blocker Code Smell",
            "S2699:Tests should include assertions",
            Justification = ProbeJustification)]
        public async Task Scenario_Should_StillPass() => await World.Ticks(1);
    }

    /// <summary>Probe for the healthy-rollback-stays-quiet test.</summary>
    private sealed class QuietProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(RollbackWorld = true)]
        [SuppressMessage(
            "Blocker Code Smell",
            "S2699:Tests should include assertions",
            Justification = ProbeJustification)]
        public async Task Scenario_Should_PassSilently() => await World.Ticks(1);
    }

    /// <summary>Probe for the restart-cost-in-output test.</summary>
    private sealed class RestartOutputProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(RestartWorld = true)]
        [SuppressMessage(
            "Blocker Code Smell",
            "S2699:Tests should include assertions",
            Justification = ProbeJustification)]
        public async Task Scenario_Should_StillPass() => await World.Ticks(1);
    }

    /// <summary>Probe for the FreshWorld-only summary test (issue #71).</summary>
    private sealed class FreshWorldOnlyProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage(
            "Blocker Code Smell",
            "S2699:Tests should include assertions",
            Justification = ProbeJustification)]
        public async Task Scenario_Should_Pass() => await World.Ticks(1);
    }

    /// <summary>Probe for the strict-isolation failure test.</summary>
    private sealed class StrictProbeScenarios : AtlasScenarioBase
    {
        /// <summary>Gets a value indicating whether the scenario body executed: strict isolation
        /// must fail the scenario BEFORE its body runs.</summary>
        public static bool BodyRan { get; private set; }

        [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
        [SuppressMessage(
            "Blocker Code Smell",
            "S2699:Tests should include assertions",
            Justification = ProbeJustification)]
        public async Task Scenario_Should_NotRun()
        {
            BodyRan = true;
            await World.Ticks(1);
        }
    }

    /// <summary>Probe for the crash-is-never-relabelled test.</summary>
    private sealed class CrashProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
        [SuppressMessage(
            "Blocker Code Smell",
            "S2699:Tests should include assertions",
            Justification = ProbeJustification)]
        public async Task Scenario_Should_NotRun() => await World.Ticks(1);
    }

    /// <summary>Probe whose isolation history the summary test builds up.</summary>
    private sealed class SummaryProbeScenarios
    {
    }

    /// <summary>Marker class the summary test hands the host off to.</summary>
    private sealed class SummaryHandoffScenarios
    {
    }

#pragma warning restore xUnit1000
}
