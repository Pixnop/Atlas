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

    /// <summary>Initializes a new instance of the <see cref="AtlasTestInvoker"/> class.</summary>
    /// <param name="freshWorld">Whether this scenario recycles the class host before running.</param>
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
        => _freshWorld = freshWorld;

    /// <inheritdoc />
    /// <remarks>Runs on xUnit's own test-execution thread up to the point where the reflected call
    /// is handed to <see cref="ServerHost.RunOnGameThreadAsync"/>; the awaited continuation resumes
    /// off the game thread's <c>SynchronizationContext</c> because that hand-off is a plain
    /// <see cref="Task"/> awaited with <c>ConfigureAwait(false)</c>, not xUnit's
    /// <c>AsyncTestSyncContext</c>.</remarks>
    protected override async Task<decimal> InvokeTestMethodAsync(object testClassInstance)
    {
        if (testClassInstance is AtlasScenarioBase scenario)
        {
            ServerHost host = _freshWorld
                ? await HostRegistry.RecycleAsync(TestClass).ConfigureAwait(false)
                : await HostRegistry.GetOrCreateAsync(TestClass).ConfigureAwait(false);

            decimal elapsed = 0m;
            await host.RunScenarioAsync(async world =>
            {
                scenario.World = world;
                elapsed = await base.InvokeTestMethodAsync(testClassInstance).ConfigureAwait(false);
            }).ConfigureAwait(false);
            return elapsed;
        }

        throw new AtlasSetupException(
            $"'{TestClass.FullName}' must derive from {nameof(AtlasScenarioBase)} to use [AtlasScenario].");
    }
}
