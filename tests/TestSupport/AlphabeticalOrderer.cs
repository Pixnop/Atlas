using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.TestSupport;

/// <summary>Orders test cases by method name. xUnit guarantees no execution order within a
/// class by default; a handful of Atlas's own scenario classes need one scenario to run
/// strictly before another for the sequence they exercise to mean anything (see the classes
/// listed in ADR 0010, docs/adr/0010-scenario-ordering.md). Linked (not referenced) into each
/// test project that needs it, via <c>&lt;Compile Include="..." Link="..." /&gt;</c>, so it
/// compiles into that project's own assembly and its <c>[TestCaseOrderer(...)]</c>
/// assembly-qualified name still resolves without a project reference between the two.</summary>
public class AlphabeticalOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => testCases.OrderBy(tc => tc.TestMethod.Method.Name, StringComparer.Ordinal);
}
