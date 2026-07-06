using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>Runs a single <see cref="AtlasTestCase"/>, substituting <see cref="AtlasTestRunner"/> for
/// xUnit's default <see cref="XunitTestRunner"/> so the test method body runs on the game thread.</summary>
internal sealed class AtlasTestCaseRunner : XunitTestCaseRunner
{
    private readonly AtlasTestCase _atlasTestCase;

    /// <summary>Initializes a new instance of the <see cref="AtlasTestCaseRunner"/> class.</summary>
    /// <param name="testCase">The Atlas test case to run.</param>
    /// <param name="displayName">The test's display name.</param>
    /// <param name="skipReason">The skip reason, if the test is skipped.</param>
    /// <param name="constructorArguments">Arguments to pass to the test class constructor.</param>
    /// <param name="testMethodArguments">Arguments to pass to the test method.</param>
    /// <param name="messageBus">The message bus to report results to.</param>
    /// <param name="aggregator">The exception aggregator.</param>
    /// <param name="cancellationTokenSource">The cancellation token source for the run.</param>
    public AtlasTestCaseRunner(
        AtlasTestCase testCase,
        string displayName,
        string skipReason,
        object[] constructorArguments,
        object[] testMethodArguments,
        IMessageBus messageBus,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
        : base(testCase, displayName, skipReason, constructorArguments, testMethodArguments, messageBus, aggregator, cancellationTokenSource)
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
