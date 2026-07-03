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
    private readonly int _timeoutMs;

    /// <summary>Initializes a new instance of the <see cref="AtlasTestInvoker"/> class.</summary>
    /// <param name="freshWorld">Whether this scenario recycles the class host before running.</param>
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
        _timeoutMs = timeoutMs;
    }

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
            ServerHost host = _freshWorld
                ? await HostRegistry.RecycleAsync(TestClass).ConfigureAwait(false)
                : await HostRegistry.GetOrCreateAsync(TestClass).ConfigureAwait(false);

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
}
