using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>Runs a single Atlas scenario test. Overrides <see cref="InvokeTestMethodAsync"/>, the
/// single point where xUnit reflects into the test method body, to marshal that call onto the
/// embedded game server's game thread through <see cref="AtlasTestInvoker"/> instead of running it
/// inline on xUnit's own test-execution thread. Also overrides <see cref="InvokeTestAsync"/>, the
/// point where xUnit finalizes the test's aggregated output string, to append the invoker's
/// isolation report when a rollback request degraded or a restart completed: that string travels
/// inside the TestPassed/TestFailed message, so the degrade (or the restart's cost) is visible
/// in the IDE test explorer, the TRX report and `atlas run`, not only on stderr.</summary>
internal sealed class AtlasTestRunner : XunitTestRunner
{
    private readonly ScenarioSettings _settings;

    private AtlasTestInvoker? _invoker;

    /// <summary>Initializes a new instance of the <see cref="AtlasTestRunner"/> class.</summary>
    /// <param name="settings">The scenario's isolation flags and watchdog timeout.</param>
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
        ScenarioSettings settings,
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
        => _settings = settings;

    /// <inheritdoc />
    /// <remarks>Appends the invoker's isolation report, when there is one (degraded rollback
    /// or completed restart), to the output string xUnit carries in the test's result message,
    /// and also queues it as a live
    /// <see cref="TestOutput"/> message for runners that stream output. This is the strongest
    /// always-attached channel the xUnit v2 pipeline offers: it lands in the TRX report's
    /// per-test StdOut, the IDE test explorer's output pane and `atlas run`'s per-test output,
    /// for passed and failed scenarios alike.</remarks>
    protected override async Task<Tuple<decimal, string>> InvokeTestAsync(ExceptionAggregator aggregator)
    {
        Tuple<decimal, string> result = await base.InvokeTestAsync(aggregator).ConfigureAwait(false);

        // The aggregator holds this scenario's failure (test body, class construction, watchdog
        // timeout, dead-host fail-fast alike) right here, before xUnit turns it into a result
        // message: record it so the registry keeps the class's scratch directories from the
        // first red scenario on (issue #83).
        if (aggregator.HasExceptions)
        {
            ScratchLedger.RecordFailure(TestClass);
        }

        string? report = _invoker?.IsolationReport;
        if (string.IsNullOrEmpty(report))
        {
            return result;
        }

        string line = report + Environment.NewLine;
        MessageBus.QueueMessage(new TestOutput(Test, line));
        return Tuple.Create(result.Item1, result.Item2 + line);
    }

    /// <inheritdoc />
    protected override Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
    {
        _invoker = new AtlasTestInvoker(
            _settings,
            Test,
            MessageBus,
            TestClass,
            ConstructorArguments,
            TestMethod,
            TestMethodArguments,
            BeforeAfterAttributes,
            aggregator,
            CancellationTokenSource);
        return _invoker.RunAsync();
    }
}
