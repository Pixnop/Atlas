# 0008. Boot diagnostics recorded from the static engine logger, declared per class

Status: accepted (`docs/specs/2026-09-23-boot-diagnostics.md`).

## Context

The engine already detects a broken asset at boot (malformed JSON, a wrong-typed property, a
recipe ingredient that does not resolve) and logs it, but only to `server-main.log`, which a
scenario has no way to read. Making that assertable needs two decisions: where to tap the
engine's logging, and where a scenario class declares that it wants a broken boot to fail loudly.

The tap has to exist before any asset loads, which is before `ServerMain.Launch()` and well before
the bridge mod hands a scenario its `ICoreServerAPI`, so nothing reachable from inside a scenario
can be the source; it has to be something `ServerHost` itself hooks during its own boot sequence.

## Decision

Subscribe directly to `ServerMain.Logger.EntryAdded`, the public `ILogger` event every mod author
already has through `api.Logger`, right after `ConfigureEngineStatics` creates the logger and
before `PreLaunch()`/`Launch()`. Not routed through `EngineCompat`
(0003-engine-compatibility-by-shape-probing.md): `ILogger` is public mod-facing API, not an
internal member with a shape known to move between supported engine versions, and
`ServerMain.Logger` is already read and written directly, unguarded, at that same call site.

The opt-in that turns a non-empty recording into a boot failure,
`StrictBootDiagnostics`, is declared on `AtlasWorldAttribute` (class-level), not as a new
attribute or an assembly-level switch. It shares `AtlasWorldAttribute`'s own lifecycle (one host
per class, 0002-one-live-host-per-process.md) and sits at the same granularity as the nearest
existing precedent, `AtlasScenarioAttribute.StrictIsolation`. An assembly-level switch would force
every scenario class in a project into the same choice, wrong for a project whose classes stage
different mods-under-test.

Recording keeps only entries about the mod under test: `BootDiagnosticsLog.Add` recognizes and
discards the engine's own `Server overloaded. A tick took {N}ms to complete.` warning before it
is ever recorded (2026-09-23 CI follow-up, `docs/specs/2026-09-23-boot-diagnostics.md`
"Environmental noise"; the shape was measured across 138 clean-boot logs from real CI runs). It is
dropped at the source rather than kept and flagged: a flag would add a field to the public
`BootDiagnosticEntry` record, and everything that reads `BootDiagnostics` (a scenario, the strict
check, the tests) would have to know to apply it. Filtering in `BootDiagnosticsLog.Add` keeps the
public shapes (`BootDiagnosticEntry`, `IWorldSession.BootDiagnostics`) exactly as simple as before,
and it is the only place a raw entry is turned into a diagnostic at all, so one filter there covers
every consumer, including `StrictBootDiagnostics`.

## Consequences

- One subscription point covers the engine's own boot-time logging and anything a mod logs
  through its own `Mod.Logger` (measured: it forwards into the same event, prefixed), for the
  whole host lifetime, with no separate "and during the scenario" toggle needed.
- A future engine version that changes `ILogger`'s shape is caught by ci.yml's newest-version
  lane (currently 1.22.7) and the compat.yml sweep's watch above the floor, the same net every
  other unguarded direct API call in this codebase already relies on, not by a dedicated probe;
  this was accepted rather than adding one because `ILogger` is stable public mod API, not one of
  `EngineCompat`'s documented internal-shape targets. Measured directly on 1.21.7, 1.22.3 and
  1.22.7 for this pass: `ILogger.EntryAdded` and `LogEntryDelegate` are identical on all three.
- `StrictBootDiagnostics` composes freely with every other `AtlasWorldAttribute` member (`Seed`,
  `SaveFile`, `Mods`, ...): unlike `StrictIsolation`, it has no companion mode it only makes sense
  paired with, so there is no combination to reject as a setup error.
- `StrictBootDiagnostics` cannot fail from machine load alone: the one measured
  environmental message shape never reaches the recorder, so a busy CI runner cannot turn a clean
  boot into a false positive.

## Source files

- `src/Atlas/Internal/Hosting/ServerHost.cs`: the subscription in `BootServer`, the strict check
  in `FinishBoot`.
- `src/Atlas/Internal/Diagnostics/BootDiagnosticsLog.cs`: the pure recording/filtering core this
  feeds (also listed under 0005-pure-decision-core-thin-io-shell.md).
- `src/Atlas.XUnit/AtlasWorldAttribute.cs`: `StrictBootDiagnostics`.
- `src/Atlas.XUnit/Internal/AttributeMapper.cs`: mapped onto `WorldOptions` alongside `SaveFile`.
