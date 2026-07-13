using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>A test case for an <see cref="AtlasTheoryAttribute"/>-decorated method whose data rows
/// could not be pre-enumerated at discovery time (non-serializable data, or pre-enumeration
/// disabled). Enumerates the rows at run time, exactly like xUnit's own
/// <see cref="XunitTheoryTestCase"/>, but runs each row's method body on the embedded game
/// server's game thread through the Atlas runner chain.</summary>
internal sealed class AtlasTheoryTestCase : XunitTheoryTestCase
{
    private bool _freshWorld;
    private bool _rollbackWorld;
    private bool _restartWorld;
    private bool _strictIsolation;
    private int _timeoutMs;

    /// <summary>Initializes a new instance of the <see cref="AtlasTheoryTestCase"/> class for
    /// deserialization. Called by the xUnit runner infrastructure only.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public AtlasTheoryTestCase()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AtlasTheoryTestCase"/> class.</summary>
    /// <param name="diagnosticMessageSink">Sink for diagnostic messages, supplied by the xUnit runner.</param>
    /// <param name="defaultMethodDisplay">The default test display name format.</param>
    /// <param name="defaultMethodDisplayOptions">The default test display name options.</param>
    /// <param name="testMethod">The decorated test method.</param>
    /// <param name="freshWorld">Whether each data row recycles the class host before running.</param>
    /// <param name="rollbackWorld">Whether each data row rolls the class host's world back to its
    /// snapshot before running.</param>
    /// <param name="restartWorld">Whether each data row restarts the class host before running,
    /// carrying the persisted world over onto the replacement host.</param>
    /// <param name="strictIsolation">Whether a degraded rollback fails the data row instead of
    /// silently falling back to a full host recycle.</param>
    /// <param name="timeoutMs">The maximum time, in milliseconds, each data row is allowed to run.</param>
    public AtlasTheoryTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        bool freshWorld,
        bool rollbackWorld,
        bool restartWorld,
        bool strictIsolation,
        int timeoutMs)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
        _freshWorld = freshWorld;
        _rollbackWorld = rollbackWorld;
        _restartWorld = restartWorld;
        _strictIsolation = strictIsolation;
        _timeoutMs = timeoutMs;
    }

    /// <summary>Gets a value indicating whether each data row recycles the class host before
    /// running, giving it a fresh world instead of the one shared by the test class.</summary>
    public bool FreshWorld => _freshWorld;

    /// <summary>Gets a value indicating whether each data row rolls the class host's world back
    /// to its snapshot before running, the cheap alternative to <see cref="FreshWorld"/>.</summary>
    public bool RollbackWorld => _rollbackWorld;

    /// <summary>Gets a value indicating whether each data row restarts the class host before
    /// running: graceful shutdown, then a replacement host booted against the persisted save,
    /// so the world carries over across a real save/load round trip.</summary>
    public bool RestartWorld => _restartWorld;

    /// <summary>Gets a value indicating whether a degraded rollback fails the data row instead
    /// of silently falling back to a full host recycle.</summary>
    public bool StrictIsolation => _strictIsolation;

    /// <summary>Gets the maximum time, in milliseconds, each data row is allowed to run before
    /// the off-thread watchdog fails it.</summary>
    public int TimeoutMs => _timeoutMs;

    /// <inheritdoc />
    public override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        data.AddValue(nameof(FreshWorld), _freshWorld);
        data.AddValue(nameof(RollbackWorld), _rollbackWorld);
        data.AddValue(nameof(RestartWorld), _restartWorld);
        data.AddValue(nameof(StrictIsolation), _strictIsolation);
        data.AddValue(nameof(TimeoutMs), _timeoutMs);
    }

    /// <inheritdoc />
    public override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        _freshWorld = data.GetValue<bool>(nameof(FreshWorld));
        _rollbackWorld = data.GetValue<bool>(nameof(RollbackWorld));
        _restartWorld = data.GetValue<bool>(nameof(RestartWorld));
        _strictIsolation = data.GetValue<bool>(nameof(StrictIsolation));
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
        var runner = new AtlasTheoryTestCaseRunner(
            this,
            DisplayName,
            SkipReason,
            constructorArguments,
            diagnosticMessageSink,
            messageBus,
            aggregator,
            cancellationTokenSource);
        return runner.RunAsync();
    }
}
