# 0006. Outcome objects, not exceptions, on expected degrade paths

Status: accepted (`docs/specs/2026-07-06-world-snapshot-rollback.md`).

## Context

A full host recycle costs a boot. World rollback exists to give a scenario a clean world for
a fraction of that, by restoring a snapshot in place. But a rollback has honest limits: a
suspend window it cannot acquire, an engine whose layout drifted, a mod hook that throws,
state it cannot see. When one of those hits, the safe answer is a full recycle, which is
slower but always correct.

If that path throws, every caller has to catch it, and the distinction between "the run is
broken" and "the run is slower than you hoped" disappears into the exception hierarchy.
Meanwhile the speedup is sometimes the point: a scenario can legitimately want a silent
degrade to fail it rather than pass slowly.

## Decision

Expected paths return an outcome object carrying what happened and why. A rollback attempt
reports whether the world is in the snapshot state, whether this attempt captured rather
than restored, and, when it degraded, a structured reason plus a one-line detail. The
registry falls back to a recycle, records the degrade in the class's isolation tally and
reports it in the scenario's own test output. The rollback never fails the boot.

Exceptions stay for the unexpected: a crashed host, a setup error, a scenario timeout.
`StrictIsolation` is the opt-in that converts a degrade into a failure, for scenarios where
the speedup is a contract.

Degrade wording has a single owner, so the reason enum, the log line and the test output
cannot drift apart.

## Consequences

- A degrade is visible without being fatal, and its cost is in the end-of-class summary
  instead of being paid invisibly.
- Combinations that cannot degrade are setup errors rather than silent no-ops:
  `StrictIsolation` without `RollbackWorld`, and `RestartWorld` with `StrictIsolation`.
- Degrade reasons accumulate history. Members no longer produced by any code path are kept
  so that recorded summaries and logs stay readable.
- The reason and its detail travel as a loose pair, which the call sites dereference behind
  null-forgiving operators. Pairing them in one record is a known cleanup.

## Source files

- `src/Atlas/Internal/Rollback/RollbackAttempt.cs:6`: the attempt outcome, with the reason
  and detail at `:27`-`:33`.
- `src/Atlas/Internal/Rollback/RollbackDegrade.cs`: `Classify` at `:14`, `Describe` at `:26`,
  the single wording source.
- `src/Atlas/Internal/Rollback/RollbackDegradeReason.cs`: the reasons, including the two kept
  for history at `:14` and `:22`.
- `src/Atlas.XUnit/Internal/HostRegistry.cs:120`: `RollbackOrRecycleAsync`, the fallback and
  the tally.
- `src/Atlas.XUnit/Internal/RollbackOutcome.cs`, `RecycleOutcome.cs:13`, `RestartOutcome.cs`,
  `src/Atlas/Internal/Bootstrap/EngineStager.cs` `Outcome`, `src/Atlas.Cli/StageFileResult.cs`,
  `src/Atlas.Cli/CliParseResult.cs`: the same shape elsewhere.
- `src/Atlas.XUnit/AtlasScenarioAttribute.cs:87`: `StrictIsolation` as the opt-in.
