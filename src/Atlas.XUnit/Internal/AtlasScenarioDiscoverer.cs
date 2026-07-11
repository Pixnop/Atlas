using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>Discovers <see cref="AtlasScenarioAttribute"/>-decorated test methods and produces
/// <see cref="AtlasTestCase"/> instances for them, in place of the default xUnit test case.</summary>
internal sealed class AtlasScenarioDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnosticMessageSink;

    /// <summary>Initializes a new instance of the <see cref="AtlasScenarioDiscoverer"/> class.</summary>
    /// <param name="diagnosticMessageSink">Sink for diagnostic messages, supplied by the xUnit runner.</param>
    public AtlasScenarioDiscoverer(IMessageSink diagnosticMessageSink) => _diagnosticMessageSink = diagnosticMessageSink;

    /// <summary>Produces the test case(s) for a single <see cref="AtlasScenarioAttribute"/>-decorated method.</summary>
    /// <param name="discoveryOptions">Discovery options supplied by the xUnit runner.</param>
    /// <param name="testMethod">The decorated test method.</param>
    /// <param name="factAttribute">The reflected <see cref="AtlasScenarioAttribute"/> instance.</param>
    /// <returns>A single <see cref="AtlasTestCase"/> for the method.</returns>
    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        bool freshWorld = factAttribute.GetNamedArgument<bool>(nameof(AtlasScenarioAttribute.FreshWorld));
        bool rollbackWorld = factAttribute.GetNamedArgument<bool>(nameof(AtlasScenarioAttribute.RollbackWorld));
        bool strictIsolation = factAttribute.GetNamedArgument<bool>(nameof(AtlasScenarioAttribute.StrictIsolation));
        int timeoutMs = factAttribute.GetNamedArgument<int>(nameof(AtlasScenarioAttribute.TimeoutMs));

        yield return new AtlasTestCase(
            _diagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod,
            freshWorld,
            rollbackWorld,
            strictIsolation,
            timeoutMs);
    }
}
