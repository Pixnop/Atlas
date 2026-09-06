using System.Diagnostics.CodeAnalysis;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>A test case for an <see cref="AtlasScenarioAttribute"/>-decorated method, or for one
/// pre-enumerated data row of an <see cref="AtlasTheoryAttribute"/>-decorated method. Runs the
/// reflected method body on the embedded game server's game thread, via <see cref="HostRegistry"/>,
/// instead of xUnit's default in-process reflection invoke.</summary>
internal sealed class AtlasTestCase : XunitTestCase
{
    private ScenarioSettings _settings = ScenarioSettings.None;

    /// <summary>Initializes a new instance of the <see cref="AtlasTestCase"/> class for deserialization.
    /// Called by the xUnit runner infrastructure only.</summary>
    [SuppressMessage(
        "Info Code Smell",
        "S1133:Deprecated code should be removed",
        Justification = "Not an Atlas deprecation with a period to run out: xUnit's de-serializer needs this parameterless constructor, and the base XunitTestCase() it chains to carries the same attribute, so dropping it here is a CS0618 (an error under TreatWarningsAsErrors). The text is xUnit's own.")]
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public AtlasTestCase()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AtlasTestCase"/> class.</summary>
    /// <param name="diagnosticMessageSink">Sink for diagnostic messages, supplied by the xUnit runner.</param>
    /// <param name="defaultMethodDisplay">The default test display name format.</param>
    /// <param name="defaultMethodDisplayOptions">The default test display name options.</param>
    /// <param name="testMethod">The decorated test method.</param>
    /// <param name="settings">The scenario's isolation flags and watchdog timeout.</param>
    /// <param name="testMethodArguments">The pre-enumerated data row for a theory scenario
    /// (serialized by the <see cref="XunitTestCase"/> base), or <see langword="null"/> for a
    /// plain fact-style scenario.</param>
    public AtlasTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        ScenarioSettings settings,
        object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
        => _settings = settings;

    /// <summary>Gets this scenario's isolation flags and watchdog timeout, as declared on its
    /// <see cref="AtlasScenarioAttribute"/> (or, for one pre-enumerated data row, its
    /// <see cref="AtlasTheoryAttribute"/>) and carried across xUnit's serialization round
    /// trip.</summary>
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
