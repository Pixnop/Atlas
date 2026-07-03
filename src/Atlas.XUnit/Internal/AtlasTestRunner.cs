using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>Runs a single Atlas scenario test. Overrides <see cref="InvokeTestMethodAsync"/>, the
/// single point where xUnit reflects into the test method body, to marshal that call onto the
/// embedded game server's game thread through <see cref="AtlasTestInvoker"/> instead of running it
/// inline on xUnit's own test-execution thread.</summary>
internal sealed class AtlasTestRunner : XunitTestRunner
{
    private readonly bool _freshWorld;
    private readonly int _timeoutMs;

    /// <summary>Initializes a new instance of the <see cref="AtlasTestRunner"/> class.</summary>
    /// <param name="freshWorld">Whether this scenario recycles the class host before running.</param>
    /// <param name="timeoutMs">The maximum time, in milliseconds, the scenario is allowed to run
    /// before the off-thread watchdog fails it.</param>
    /// <param name="test">The test being run.</param>
    /// <param name="messageBus">The message bus to report results to.</param>
    /// <param name="testClass">The scenario class.</param>
    /// <param name="constructorArguments">Arguments to pass to the test class constructor.</param>
    /// <param name="testMethod">The reflected test method.</param>
    /// <param name="testMethodArguments">Arguments to pass to the test method.</param>
    /// <param name="skipReason">The skip reason, if the test is skipped.</param>
    /// <param name="beforeAfterAttributes">Before/after attributes decorating the test.</param>
    /// <param name="aggregator">The exception aggregator.</param>
    /// <param name="cancellationTokenSource">The cancellation token source for the run.</param>
    public AtlasTestRunner(
        bool freshWorld,
        int timeoutMs,
        ITest test,
        IMessageBus messageBus,
        Type testClass,
        object[] constructorArguments,
        MethodInfo testMethod,
        object[] testMethodArguments,
        string skipReason,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, aggregator, cancellationTokenSource)
    {
        _freshWorld = freshWorld;
        _timeoutMs = timeoutMs;
    }

    /// <inheritdoc />
    protected override Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
    {
        var invoker = new AtlasTestInvoker(
            _freshWorld,
            _timeoutMs,
            Test,
            MessageBus,
            TestClass,
            ConstructorArguments,
            TestMethod,
            TestMethodArguments,
            BeforeAfterAttributes,
            aggregator,
            CancellationTokenSource);
        return invoker.RunAsync();
    }
}
