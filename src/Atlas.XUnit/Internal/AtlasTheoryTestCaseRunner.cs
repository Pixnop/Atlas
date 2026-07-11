using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>Runs a single <see cref="AtlasTheoryTestCase"/>: xUnit's own
/// <see cref="XunitTheoryTestCaseRunner"/> keeps doing the run-time data enumeration (one test per
/// row, display name including the row's arguments), and only the per-row test runner is swapped
/// for <see cref="AtlasTestRunner"/> so each row's method body runs on the game thread.</summary>
internal sealed class AtlasTheoryTestCaseRunner : XunitTheoryTestCaseRunner
{
    private readonly AtlasTheoryTestCase _atlasTestCase;

    /// <summary>Initializes a new instance of the <see cref="AtlasTheoryTestCaseRunner"/> class.</summary>
    /// <param name="testCase">The Atlas theory test case to run.</param>
    /// <param name="displayName">The test's display name.</param>
    /// <param name="skipReason">The skip reason, if the test is skipped.</param>
    /// <param name="constructorArguments">Arguments to pass to the test class constructor.</param>
    /// <param name="diagnosticMessageSink">Sink for diagnostic messages, supplied by the xUnit runner.</param>
    /// <param name="messageBus">The message bus to report results to.</param>
    /// <param name="aggregator">The exception aggregator.</param>
    /// <param name="cancellationTokenSource">The cancellation token source for the run.</param>
    public AtlasTheoryTestCaseRunner(
        AtlasTheoryTestCase testCase,
        string displayName,
        string skipReason,
        object[] constructorArguments,
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(testCase, displayName, skipReason, constructorArguments, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource)
        => _atlasTestCase = testCase;

    /// <inheritdoc />
    protected override XunitTestRunner CreateTestRunner(
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
        => new AtlasTestRunner(
            _atlasTestCase.FreshWorld,
            _atlasTestCase.RollbackWorld,
            restartWorld: false,
            strictIsolation: false,
            _atlasTestCase.TimeoutMs,
            test,
            messageBus,
            testClass,
            constructorArguments,
            testMethod,
            testMethodArguments,
            skipReason,
            beforeAfterAttributes,
            aggregator,
            cancellationTokenSource);
}
