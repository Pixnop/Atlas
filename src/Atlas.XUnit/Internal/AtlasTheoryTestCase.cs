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
    private ScenarioSettings _settings = ScenarioSettings.None;

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
    /// <param name="settings">The isolation flags and watchdog timeout every data row runs
    /// under.</param>
    public AtlasTheoryTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        ScenarioSettings settings)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
        => _settings = settings;

    /// <summary>Gets the isolation flags and watchdog timeout every data row of this theory runs
    /// under, as declared on its <see cref="AtlasTheoryAttribute"/> and carried across xUnit's
    /// serialization round trip.</summary>
    public ScenarioSettings Settings => _settings;

    /// <inheritdoc />
    public override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        _settings.Write(data);
    }

    /// <inheritdoc />
    public override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        _settings = ScenarioSettings.Read(data);
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
