using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>A test case for an <see cref="AtlasScenarioAttribute"/>-decorated method. Runs the
/// reflected method body on the embedded game server's game thread, via <see cref="HostRegistry"/>,
/// instead of xUnit's default in-process reflection invoke.</summary>
internal sealed class AtlasTestCase : XunitTestCase
{
    private bool _freshWorld;
    private bool _rollbackWorld;
    private int _timeoutMs;

    /// <summary>Initializes a new instance of the <see cref="AtlasTestCase"/> class for deserialization.
    /// Called by the xUnit runner infrastructure only.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public AtlasTestCase()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AtlasTestCase"/> class.</summary>
    /// <param name="diagnosticMessageSink">Sink for diagnostic messages, supplied by the xUnit runner.</param>
    /// <param name="defaultMethodDisplay">The default test display name format.</param>
    /// <param name="defaultMethodDisplayOptions">The default test display name options.</param>
    /// <param name="testMethod">The decorated test method.</param>
    /// <param name="freshWorld">Whether this scenario recycles the class host before running.</param>
    /// <param name="rollbackWorld">Whether this scenario rolls the class host's world back to its
    /// snapshot before running.</param>
    /// <param name="timeoutMs">The maximum time, in milliseconds, the scenario is allowed to run.</param>
    /// <remarks><paramref name="timeoutMs"/> is carried as plain data, NOT mapped onto
    /// <see cref="XunitTestCase.Timeout"/>: see <see cref="AtlasScenarioAttribute.TimeoutMs"/> for why.
    /// It is enforced by an off-thread <c>Watchdog</c> inside <c>AtlasTestInvoker</c> instead.</remarks>
    public AtlasTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        bool freshWorld,
        bool rollbackWorld,
        int timeoutMs)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
        _freshWorld = freshWorld;
        _rollbackWorld = rollbackWorld;
        _timeoutMs = timeoutMs;
    }

    /// <summary>Gets a value indicating whether this scenario recycles the class host before running,
    /// giving it a fresh world instead of the one shared by the test class.</summary>
    public bool FreshWorld => _freshWorld;

    /// <summary>Gets a value indicating whether this scenario rolls the class host's world back to
    /// its snapshot before running, the cheap alternative to <see cref="FreshWorld"/>.</summary>
    public bool RollbackWorld => _rollbackWorld;

    /// <summary>Gets the maximum time, in milliseconds, the scenario is allowed to run before the
    /// off-thread watchdog fails it.</summary>
    public int TimeoutMs => _timeoutMs;

    /// <inheritdoc />
    public override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        data.AddValue(nameof(FreshWorld), _freshWorld);
        data.AddValue(nameof(RollbackWorld), _rollbackWorld);
        data.AddValue(nameof(TimeoutMs), _timeoutMs);
    }

    /// <inheritdoc />
    public override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        _freshWorld = data.GetValue<bool>(nameof(FreshWorld));
        _rollbackWorld = data.GetValue<bool>(nameof(RollbackWorld));
        _timeoutMs = data.GetValue<int>(nameof(TimeoutMs));
    }

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var runner = new AtlasTestCaseRunner(
            this,
            DisplayName,
            SkipReason,
            constructorArguments,
            TestMethodArguments,
            messageBus,
            aggregator,
            cancellationTokenSource);
        return runner.RunAsync();
    }
}
