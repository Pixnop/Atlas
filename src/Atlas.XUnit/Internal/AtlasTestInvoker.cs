using System.Reflection;
using Atlas.Api;
using Atlas.Internal.Hosting;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>Invokes an Atlas scenario method body on the embedded game server's game thread.
/// Overrides <see cref="XunitTestInvoker.InvokeTestMethodAsync"/>, xUnit's own reflected-call-plus-await
/// point, so the base's timing, timeout, and Task-awaiting behavior are preserved; only the call
/// itself is marshaled onto the game thread through <see cref="ServerHost.RunOnGameThreadAsync"/>.</summary>
internal sealed class AtlasTestInvoker : XunitTestInvoker
{
    private readonly bool _freshWorld;
    private readonly bool _rollbackWorld;
    private readonly bool _restartWorld;
    private readonly bool _strictIsolation;
    private readonly int _timeoutMs;

    /// <summary>Initializes a new instance of the <see cref="AtlasTestInvoker"/> class.</summary>
    /// <param name="freshWorld">Whether this scenario recycles the class host before running.</param>
    /// <param name="rollbackWorld">Whether this scenario rolls the class host's world back to its
    /// snapshot before running.</param>
    /// <param name="restartWorld">Whether this scenario restarts the class host before running,
    /// carrying the persisted world over onto the replacement host.</param>
    /// <param name="strictIsolation">Whether a degraded rollback fails this scenario instead of
    /// silently falling back to a full host recycle.</param>
    /// <param name="timeoutMs">The maximum time, in milliseconds, the scenario is allowed to run
    /// before the off-thread watchdog fails it.</param>
    /// <param name="test">The test being run.</param>
    /// <param name="messageBus">The message bus to report results to.</param>
    /// <param name="testClass">The scenario class.</param>
    /// <param name="constructorArguments">Arguments to pass to the test class constructor.</param>
    /// <param name="testMethod">The reflected test method.</param>
    /// <param name="testMethodArguments">Arguments to pass to the test method.</param>
    /// <param name="beforeAfterAttributes">Before/after attributes decorating the test.</param>
    /// <param name="aggregator">The exception aggregator.</param>
    /// <param name="cancellationTokenSource">The cancellation token source for the run.</param>
    public AtlasTestInvoker(
        bool freshWorld,
        bool rollbackWorld,
        bool restartWorld,
        bool strictIsolation,
        int timeoutMs,
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, beforeAfterAttributes, aggregator, cancellationTokenSource)
    {
        _freshWorld = freshWorld;
        _rollbackWorld = rollbackWorld;
        _restartWorld = restartWorld;
        _strictIsolation = strictIsolation;
        _timeoutMs = timeoutMs;
    }

    /// <summary>Gets the isolation report of this scenario, or <see langword="null"/> when its
    /// isolation request was honored as asked. Set when a RollbackWorld request degraded to a
    /// full host recycle; <see cref="AtlasTestRunner"/> appends it to the test's own output so
    /// the degrade is visible in the standard workflow, not only on stderr.</summary>
    public string? IsolationReport { get; private set; }

    /// <inheritdoc />
    /// <remarks>Runs on xUnit's own test-execution thread up to the point where the reflected call
    /// is handed to <see cref="ServerHost.RunOnGameThreadAsync"/>; the awaited continuation resumes
    /// off the game thread's <c>SynchronizationContext</c> because that hand-off is a plain
    /// <see cref="Task"/> awaited with <c>ConfigureAwait(false)</c>, not xUnit's
    /// <c>AsyncTestSyncContext</c>. That is also what makes the <see cref="Watchdog"/> below safe: its
    /// <c>Task.WhenAny</c> continuation runs on the thread pool, not on the game thread, so it still
    /// fires a <see cref="ScenarioTimeoutException"/> even when the game thread itself is the thing
    /// that is stuck.</remarks>
    protected override async Task<decimal> InvokeTestMethodAsync(object testClassInstance)
    {
        if (testClassInstance is AtlasScenarioBase scenario)
        {
            // Resolve isolation BEFORE touching the registry: a contradictory flag combination
            // (more than one world flag, StrictIsolation without RollbackWorld) fails the
            // scenario without booting anything.
            WorldIsolation isolation = WorldIsolationResolver.Resolve(
                Test.DisplayName, _freshWorld, _rollbackWorld, _restartWorld, _strictIsolation);
            ServerHost host = isolation switch
            {
                WorldIsolation.FreshWorld => await RecycleFreshWorldAsync().ConfigureAwait(false),
                WorldIsolation.RollbackWorld => await RollbackWorldAsync().ConfigureAwait(false),
                WorldIsolation.RestartWorld => await HostRegistry.RestartAsync(TestClass).ConfigureAwait(false),
                _ => await HostRegistry.GetOrCreateAsync(TestClass).ConfigureAwait(false),
            };

            decimal elapsed = 0m;
            Task scenarioTask = host.RunScenarioAsync(async world =>
            {
                scenario.World = world;
                elapsed = await base.InvokeTestMethodAsync(testClassInstance).ConfigureAwait(false);
            });

            try
            {
                await Watchdog.RunAsync(scenarioTask, _timeoutMs, () => host.CurrentTick).ConfigureAwait(false);
            }
            catch (ScenarioTimeoutException)
            {
                // The game thread may still be running the abandoned scenario: it cannot be aborted
                // safely, so the host is no longer trustworthy for this class either. Observe the
                // abandoned task's eventual outcome so a later fault does not surface as an
                // UnobservedTaskException once it is garbage collected.
                _ = scenarioTask.ContinueWith(
                    static t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                string message = $"'{TestClass.FullName}' host was abandoned after a scenario exceeded " +
                    $"its {_timeoutMs} ms watchdog; the game thread may still be stuck running it.";
                HostRegistry.MarkDead(TestClass, message);

                // Belt-and-suspenders: if the host already recorded a crash, the watchdog timeout is
                // only a symptom (the game thread died mid-scenario, so it never resumed the parked
                // wait and never got a chance to drain it either). Surface the true cause instead,
                // wrapped the same way ThrowIfCrashed wraps it.
                if (host.WrapCrashIfAny() is { } crashException)
                {
                    throw crashException;
                }

                throw;
            }
            catch (ServerCrashedException)
            {
                string message = $"'{TestClass.FullName}' host already crashed in an earlier scenario.";
                HostRegistry.MarkDead(TestClass, message);
                throw;
            }

            return elapsed;
        }

        throw new AtlasSetupException(
            $"'{TestClass.FullName}' must derive from {nameof(AtlasScenarioBase)} to use [AtlasScenario].");
    }

    /// <summary>Requests a FreshWorld recycle and counts it in the class's isolation tally
    /// (after the recycle succeeded, so a dead-class fail-fast is not counted as a paid boot).</summary>
    /// <returns>The freshly booted host.</returns>
    private async Task<ServerHost> RecycleFreshWorldAsync()
    {
        ServerHost host = await HostRegistry.RecycleAsync(TestClass).ConfigureAwait(false);
        IsolationLedger.RecordFreshWorldRecycle(TestClass);
        return host;
    }

    /// <summary>Requests rollback isolation and turns a degraded outcome into evidence: the
    /// isolation report attached to the test's output, and, under strict isolation, an
    /// <see cref="AtlasIsolationException"/> failing the scenario. The registry has already
    /// recycled the host by the time the outcome arrives, so even the strict failure leaves the
    /// class on a clean world; a genuine <see cref="ServerCrashedException"/> from the rollback
    /// attempt propagates out of <see cref="HostRegistry.RollbackOrRecycleAsync"/> before any
    /// outcome exists, so a crash is never re-labelled as a strictness failure.</summary>
    /// <returns>The host the scenario runs on.</returns>
    /// <exception cref="AtlasIsolationException">Thrown when the rollback degraded and this
    /// scenario requested <see cref="AtlasScenarioAttribute.StrictIsolation"/>.</exception>
    private async Task<ServerHost> RollbackWorldAsync()
    {
        RollbackOutcome outcome = await HostRegistry.RollbackOrRecycleAsync(TestClass).ConfigureAwait(false);
        if (outcome.Degraded)
        {
            IsolationReport = IsolationMessages.DegradeReport(
                outcome.DegradeReason, outcome.DegradeDetail!, outcome.RecycleCost);
            if (_strictIsolation)
            {
                throw new AtlasIsolationException(
                    IsolationMessages.StrictFailure(Test.DisplayName, outcome.DegradeReason, outcome.DegradeDetail!));
            }
        }

        return outcome.Host;
    }
}
