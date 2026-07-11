using System.Reflection;
using Atlas.Internal.Rollback;
using Atlas.XUnit;
using Atlas.XUnit.Internal;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.Engine.Tests;

/// <summary>Covers issue #53 end-to-end: a degraded rollback must be visible in the standard
/// workflow (attached to the test's own output, which travels inside the TestPassed/TestFailed
/// message into TRX, the IDE test explorer and `atlas run`), strict isolation must fail with the
/// degrade reason instead of silently recycling, a genuine crash must never be re-labelled, and
/// the registry must print a per-class isolation summary when a class hands its host off. Each
/// test drives a real <see cref="AtlasTestCase"/> through the full pipeline (case runner, test
/// runner, invoker, registry, host) against a private probe scenario class, spying on the xUnit
/// message bus: a test cannot assert its own output, so the probe's run is nested, exactly like
/// <see cref="NestedRunnerTests"/> but per test case. The induction seam is the documented one:
/// a swapped <c>WorldSnapshotFactory</c> on the probe class's live host.</summary>
[Trait("Category", "E2E")]
public class IsolationObservabilityTests
{
    [Fact]
    public async Task DegradedRollback_Should_AttachReasonAndCostToTestOutput_When_ScenarioStillPasses()
    {
        ServerHost original = await HostRegistry.GetOrCreateAsync(typeof(DegradeOutputProbeScenarios));
        original.WorldSnapshotFactory =
            (_, _) => throw new InvalidOperationException("simulated capture failure");

        IReadOnlyList<IMessageSinkMessage> messages = await RunScenarioCaseAsync(
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
    public async Task HealthyRollback_Should_StayQuiet_When_SnapshotWorks()
    {
        IReadOnlyList<IMessageSinkMessage> messages = await RunScenarioCaseAsync(
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

        IReadOnlyList<IMessageSinkMessage> messages = await RunScenarioCaseAsync(
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
            IReadOnlyList<IMessageSinkMessage> messages = await RunScenarioCaseAsync(
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
        var stderr = new StringWriter();
        TextWriter realStderr = Console.Error;
        try
        {
            Console.SetError(stderr);
            _ = await HostRegistry.GetOrCreateAsync(typeof(SummaryHandoffScenarios));
        }
        finally
        {
            Console.SetError(realStderr);
        }

        string summary = stderr.ToString();
        Assert.Contains($"[Atlas] isolation summary for {typeof(SummaryProbeScenarios).FullName}", summary);
        Assert.Contains("2 rollback(s) succeeded", summary);
        Assert.Contains("1 degraded to a full host recycle (capture or restore failed x1)", summary);
        Assert.Contains("0 FreshWorld recycle(s)", summary);
    }

    /// <summary>Runs one probe scenario through the real Atlas xUnit pipeline
    /// (<see cref="AtlasTestCase"/> down to the registry and host), collecting every message the
    /// runner reports.</summary>
    /// <param name="probeClass">The probe scenario class.</param>
    /// <param name="methodName">The scenario method to run.</param>
    /// <param name="strictIsolation">The strict-isolation flag of the synthetic test case.</param>
    /// <returns>The messages the pipeline queued, in order.</returns>
    private static async Task<IReadOnlyList<IMessageSinkMessage>> RunScenarioCaseAsync(
        Type probeClass, string methodName, bool strictIsolation)
    {
        var diagnosticSink = new NullDiagnosticSink();
        var testCase = new AtlasTestCase(
            diagnosticSink,
            Xunit.Sdk.TestMethodDisplay.ClassAndMethod,
            Xunit.Sdk.TestMethodDisplayOptions.None,
            BuildTestMethod(probeClass, methodName),
            freshWorld: false,
            rollbackWorld: true,
            restartWorld: false,
            strictIsolation: strictIsolation,
            timeoutMs: 60_000);

        using var bus = new SpyMessageBus();
        await testCase.RunAsync(
            diagnosticSink, bus, Array.Empty<object>(), new ExceptionAggregator(), new CancellationTokenSource());
        return bus.Messages;
    }

    /// <summary>Builds the xUnit test-method object graph for a probe scenario, the same shape
    /// the real discoverer produces.</summary>
    /// <param name="probeClass">The probe scenario class.</param>
    /// <param name="methodName">The scenario method.</param>
    /// <returns>The test method.</returns>
    private static TestMethod BuildTestMethod(Type probeClass, string methodName)
    {
        MethodInfo method = probeClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"probe method '{methodName}' not found on '{probeClass}'");
        var testAssembly = new TestAssembly(Reflector.Wrap(probeClass.Assembly));
        var collection = new TestCollection(testAssembly, null, "Atlas isolation observability probes");
        var testClass = new TestClass(collection, Reflector.Wrap(probeClass));
        return new TestMethod(testClass, Reflector.Wrap(method));
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
        public async Task Scenario_Should_StillPass() => await World.Ticks(1);
    }

    /// <summary>Probe for the healthy-rollback-stays-quiet test.</summary>
    private sealed class QuietProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(RollbackWorld = true)]
        public async Task Scenario_Should_PassSilently() => await World.Ticks(1);
    }

    /// <summary>Probe for the strict-isolation failure test.</summary>
    private sealed class StrictProbeScenarios : AtlasScenarioBase
    {
        /// <summary>Gets a value indicating whether the scenario body executed: strict isolation
        /// must fail the scenario BEFORE its body runs.</summary>
        public static bool BodyRan { get; private set; }

        [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
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

    /// <summary>Message bus spy: collects every message the pipeline queues.</summary>
    private sealed class SpyMessageBus : IMessageBus
    {
        private readonly List<IMessageSinkMessage> _messages = [];

        public IReadOnlyList<IMessageSinkMessage> Messages => _messages;

        public bool QueueMessage(IMessageSinkMessage message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }

            return true;
        }

        public void Dispose()
        {
            // Nothing to release; the spy only holds managed state.
        }
    }

    /// <summary>Diagnostic sink that swallows everything (the probes' diagnostics are noise).</summary>
    private sealed class NullDiagnosticSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message) => true;
    }
}
