using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.XUnit.Internal;

/// <summary>Discovers <see cref="AtlasTheoryAttribute"/>-decorated test methods and produces one
/// Atlas test case per data row, in place of xUnit's default theory test cases.</summary>
/// <remarks>Derives from xUnit's own <see cref="TheoryDiscoverer"/>, inheriting its data
/// resolution unchanged (row pre-enumeration, per-row serializability, skipped rows, the
/// "No data found for ..." failure); only the produced test cases are swapped for Atlas ones,
/// mirroring the stock <c>XunitTestCase</c>/<c>XunitTheoryTestCase</c> split.</remarks>
internal sealed class AtlasTheoryDiscoverer : TheoryDiscoverer
{
    /// <summary>Initializes a new instance of the <see cref="AtlasTheoryDiscoverer"/> class.</summary>
    /// <param name="diagnosticMessageSink">Sink for diagnostic messages, supplied by the xUnit runner.</param>
    public AtlasTheoryDiscoverer(IMessageSink diagnosticMessageSink)
        : base(diagnosticMessageSink)
    {
    }

    /// <summary>Produces the test case for one serializable, pre-enumerated data row.</summary>
    /// <param name="discoveryOptions">Discovery options supplied by the xUnit runner.</param>
    /// <param name="testMethod">The decorated test method.</param>
    /// <param name="theoryAttribute">The reflected <see cref="AtlasTheoryAttribute"/> instance.</param>
    /// <param name="dataRow">The row's test method arguments.</param>
    /// <returns>A single <see cref="AtlasTestCase"/> carrying the row as its arguments.</returns>
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo theoryAttribute,
        object[] dataRow)
    {
        bool freshWorld = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.FreshWorld));
        bool rollbackWorld = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.RollbackWorld));
        bool restartWorld = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.RestartWorld));
        bool strictIsolation = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.StrictIsolation));
        int timeoutMs = theoryAttribute.GetNamedArgument<int>(nameof(AtlasTheoryAttribute.TimeoutMs));

        yield return new AtlasTestCase(
            DiagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod,
            freshWorld,
            rollbackWorld,
            restartWorld,
            strictIsolation,
            timeoutMs,
            dataRow);
    }

    /// <summary>Produces the single runtime-enumerating test case used when the theory's rows
    /// cannot be pre-enumerated at discovery time (non-serializable data, or pre-enumeration
    /// disabled).</summary>
    /// <param name="discoveryOptions">Discovery options supplied by the xUnit runner.</param>
    /// <param name="testMethod">The decorated test method.</param>
    /// <param name="theoryAttribute">The reflected <see cref="AtlasTheoryAttribute"/> instance.</param>
    /// <returns>A single <see cref="AtlasTheoryTestCase"/> that enumerates its rows at run time.</returns>
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute)
    {
        bool freshWorld = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.FreshWorld));
        bool rollbackWorld = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.RollbackWorld));
        bool restartWorld = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.RestartWorld));
        bool strictIsolation = theoryAttribute.GetNamedArgument<bool>(nameof(AtlasTheoryAttribute.StrictIsolation));
        int timeoutMs = theoryAttribute.GetNamedArgument<int>(nameof(AtlasTheoryAttribute.TimeoutMs));

        yield return new AtlasTheoryTestCase(
            DiagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod,
            freshWorld,
            rollbackWorld,
            restartWorld,
            strictIsolation,
            timeoutMs);
    }
}
