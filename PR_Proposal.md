# PR Proposal: `[AtlasTheory]` parameterized scenarios

## Summary

This PR adds **`[AtlasTheory]`** — a theory-style counterpart to `[AtlasScenario]`, so a single
scenario method can run once per `[InlineData]` (or any other xUnit `DataAttribute`) row, each
row on the embedded server's game thread like any other Atlas scenario.

## Motivation

Scenario tests are frequently the same interaction exercised over a handful of block codes,
entity codes, or quantities. Today that means one copy-pasted `[AtlasScenario]` method per
variant. `[AtlasTheory]` removes that duplication using the xUnit idiom test authors already
know:

```csharp
public class SmeltingScenarios : AtlasScenarioBase
{
    [AtlasTheory(TimeoutMs = 30_000)]
    [InlineData("game:ore-nativecopper", 1)]
    [InlineData("game:ore-cassiterite", 2)]
    public async Task OreSmelts(string oreCode, int expectedUnits)
    {
        World.SetBlock(oreCode, World.Spawn);
        await World.Until(() => /* ... */);
        // ...
    }
}
```

## Design and implementation

The implementation follows the same shape as the existing fact-style pipeline, and reuses
xUnit v2's own theory machinery wherever possible:

- **`AtlasTheoryAttribute`** derives from `TheoryAttribute` and mirrors `AtlasScenarioAttribute`'s
  settings exactly (`FreshWorld`, `RollbackWorld`, `TimeoutMs`). Each setting applies per data
  row, since each row is a full scenario of its own (e.g. `FreshWorld = true` recycles the host
  before every row). It is a sibling of `AtlasScenarioAttribute` rather than a subclass, the same
  way xUnit keeps `TheoryAttribute` beside `FactAttribute`, so each attribute binds unambiguously
  to its own discoverer.
- **`AtlasTheoryDiscoverer`** derives from xUnit's `TheoryDiscoverer`, inheriting all of its data
  resolution verbatim: discovery-time pre-enumeration of data rows, per-row serializability
  checks, skipped rows, and the "No data found for ..." execution-error case when a theory has no
  rows. Only the two factory methods are overridden:
  - `CreateTestCasesForDataRow` yields one `AtlasTestCase` per serializable row, carrying the
    row as `TestMethodArguments` (serialized by the `XunitTestCase` base, so per-row test cases
    survive the VS Test Explorer discovery/execution round-trip and appear individually, with
    xUnit's standard argument-formatted display names).
  - `CreateTestCasesForTheory` yields a single `AtlasTheoryTestCase` for the standard xUnit v2
    fallback: non-serializable data rows, or pre-enumeration disabled.
- **`AtlasTheoryTestCase` / `AtlasTheoryTestCaseRunner`** cover that fallback. The runner derives
  from `XunitTheoryTestCaseRunner`, which keeps doing the run-time data enumeration (one result
  per row, arguments in the display name); only `CreateTestRunner` is overridden to substitute
  the existing `AtlasTestRunner`, so each row's method body is marshaled onto the game thread
  exactly like a fact-style scenario. No Atlas-specific data handling was written at all.

From `AtlasTestRunner` down, the pipeline is unchanged — theories share the invoker, watchdog,
host registry, and isolation resolution with facts. `AtlasTestCase` gained the optional
`testMethodArguments` constructor parameter (forwarded to the `XunitTestCase` base) and now
reaches the invocation with the row's arguments via the base class's existing plumbing.

## Files touched

| Area | File | Change |
|---|---|---|
| Atlas.XUnit | `AtlasTheoryAttribute.cs` | new |
| Atlas.XUnit | `Internal/AtlasTheoryDiscoverer.cs` | new |
| Atlas.XUnit | `Internal/AtlasTheoryTestCase.cs` | new |
| Atlas.XUnit | `Internal/AtlasTheoryTestCaseRunner.cs` | new |
| Atlas.XUnit | `Internal/AtlasTestCase.cs` | optional `testMethodArguments` constructor parameter |
| Atlas.XUnit | `Internal/AtlasTestInvoker.cs` | setup-error message now names `[AtlasTheory]` too |
| Tests | `AdapterTheoryTests.cs`, `TheoryNestedRunnerTests.cs`, `TheoryRowScenarios.cs`, `TheoryRowMarker.cs` | new coverage |
| Tests | `CliFacadeTests.cs`, `WorkerModeTests.cs`, `ParallelModeTests.cs`, `NestedRunnerTests.cs` | guinea pig assembly counts updated; nested run scoped to its classes |

## Backward compatibility

- `[AtlasScenario]` discovery, the CLI runner (`atlas run`, which drives the same discoverer
  pipeline through the in-process xUnit runner), and the watchdog path are untouched.
- `AtlasTestCase`'s new constructor parameter is optional and defaults to `null` (fact-style
  behavior); all existing call sites compile unchanged.

## Testing notes

Builds clean (zero warnings, `TreatWarningsAsErrors` on) and the full suite passes. Coverage, in
the repo's existing test layout:

- **Atlas.Engine.Tests / GuineaPig scenarios** (embedded server):
  - `[AtlasTheory]` + `[InlineData]`: one result per row, arguments in display names, arguments
    bound to the method (`AdapterTheoryTests`).
  - A failing row does not fail its sibling rows, asserted through the nested in-process runner
    over deliberately-failing guinea pig scenarios (`TheoryNestedRunnerTests`).

