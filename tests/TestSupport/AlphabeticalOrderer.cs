using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.TestSupport;

/// <summary>Orders test cases by method name. xUnit guarantees no execution order within a
/// class by default; a handful of Atlas's own scenario classes need one scenario to run
/// strictly before another for the sequence they exercise to mean anything (see the classes
/// listed in ADR 0010, docs/adr/0010-scenario-ordering.md). Linked (not referenced) into each
/// test project that needs it, via <c>&lt;Compile Include="..." Link="..." /&gt;</c>, so it
/// compiles into that project's own assembly and its <c>[TestCaseOrderer(...)]</c>
/// assembly-qualified name resolves within that assembly. Kept <c>internal</c> rather than
/// <c>public</c>: Atlas.Engine.Tests also has an unrelated <c>ProjectReference</c> to
/// Atlas.GuineaPig.Scenarios (for <c>NestedRunnerTests</c>), and a public type here would be
/// imported through it under that same name, so any future <c>typeof</c>/<c>nameof</c>/<c>cref</c>
/// naming this class from Atlas.Engine.Tests would hit CS0436.</summary>
internal class AlphabeticalOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => testCases.OrderBy(tc => tc.TestMethod.Method.Name, StringComparer.Ordinal);
}
