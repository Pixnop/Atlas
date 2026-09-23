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

Recording leaves out one engine message that reports machine load rather than content:
`BootDiagnosticsLog.Add` recognizes and discards the engine's own `Server overloaded. A tick took
{N}ms to complete.` warning before it is ever recorded (2026-09-23 CI follow-up,
`docs/specs/2026-09-23-boot-diagnostics.md` "Environmental noise"; the shape was measured across
138 clean-boot logs from real CI runs). The Stratum fork (which Atlas also targets, see
StratumParity) logs the same warning from its own `ServerMain`, worded "Server may be overloaded.
..." instead, so the rule accepts both wordings. It is dropped at the source rather than kept and
flagged: a flag would add a field to the public
`BootDiagnosticEntry` record, and everything that reads `BootDiagnostics` (a scenario, the strict
check, the tests) would have to know to apply it. Filtering in `BootDiagnosticsLog.Add` keeps the
public shapes (`BootDiagnosticEntry`, `IWorldSession.BootDiagnostics`) exactly as simple as before,
and it is the only place a raw entry is turned into a diagnostic at all, so one filter there covers
every consumer, including `StrictBootDiagnostics`.

## Amendment: honest source, an allowlist, and a vanilla opt-out (2026-09-23)

Field feedback on 0.14.0-rc.1 from two real consumers (Nimbus, StratumParity) named three gaps in
this design, all closed on the same branch (see the spec's "Field feedback" section for the full
measurement and reasoning):

- **`Source` was a guess.** A bracketed `"[name] "` prefix on a central-logger entry meant one of
  two different things, a verified `Mod.Logger` call or a mod's own unverified hand-written
  convention through the shared, unprefixed `api.Logger`, and the original design trusted both
  the same way, sometimes wrongly (Nimbus's own `"[Nimbus] "` is not its mod id).
  `BootDiagnosticsLog.ResolveModAttribution` first tried cross-checking every parsed hint against
  `ICoreServerAPI.ModLoader.Mods` (read once, at `FinishBoot`) instead of trusting the parse, on
  the reasoning that checking after the fact sidesteps the ordering problem a live per-`ModLogger`
  subscription would hit (a mod's own early load-time errors fire before Atlas's own bridge mod,
  itself mod code loaded in the same pass, could subscribe to anything mod-specific). That
  reasoning held for the ordering problem but missed a simpler one: a name match, checked at any
  time, is still just a name match, and a mod's own hand-written bracket can spell its real mod id
  correctly (review finding, 2026-09-23), which `ResolveModAttribution` would then verify exactly
  like a genuine `Mod.Logger` call. The actual fix (same date) subscribes to every loaded mod's
  own `Mod.Logger.EntryAdded` directly, as early as the engine allows: `Atlas.Bridge.BridgeModSystem`
  now hooks `StartPre` at the lowest possible `ExecuteOrder`, guaranteeing it runs before any other
  mod's own `StartPre` or `StartServerSide`, and publishes `ICoreAPI.ModLoader.Mods` (already fully
  populated by then, `ModLoader.LoadMods` having already run) back to `ServerHost` through the same
  AppDomain-slot rendezvous the API handoff already uses. `ModLogger.LogImpl` forwards every call
  into the central logger first (what `BootDiagnosticsLog.Add` records, still unverified) and only
  fires the mod's own `EntryAdded` afterwards, synchronously, on the same thread
  (`LoggerBase.Log`'s own order); `BootDiagnosticsLog.VerifyFromMod` uses that guaranteed ordering
  (a `[ThreadStatic]` pending-entry marker, no locking needed across the two calls) to mark the
  entry `Add` just built as verified for that exact mod, evidence by channel, not by name.
  `ResolveModAttribution`'s name match still exists, but now only for an entry recorded before any
  such subscription could exist at all: the engine's own per-mod-container load error (a missing
  `modinfo.json`, a failed assembly load), logged while the mod list itself is still being built.
  There is no channel for that case to go through instead, and no mod code has run yet at that
  point to fake a bracket, so a name match is still the right, and only available, signal there.
- **Strict mode was all-or-nothing.** A class staging a mod with one deliberate warning (by
  design, not a bug) could never turn `StrictBootDiagnostics` on for that class.
  `[AtlasAllowBootDiagnostic(pattern, Level = ..., Source = ...)]` (assembly or class,
  `AllowMultiple`) declares an exemption strict mode ignores; `BootDiagnosticsLog.Snapshot()` (and
  so `IWorldSession.BootDiagnostics`) is unaffected, so a scenario can still see what was allowed.
- **No way to boot one class without the assembly's mods.** `AtlasWorldAttribute.ExcludeAssemblyMods`
  skips both assembly-wide mod sources (the `[AtlasMods(...)]` attribute and the MSBuild-generated
  manifest) for one class, leaving its own `Mods` untouched. No `HostRegistry` change was needed:
  it already keys the live host by `Type` and disposes-and-recreates on every owner change, so two
  classes with different mod sets can never share a host regardless of this flag.

Also measured, since the release notes had not: recording's own overhead. The first pass at this
(7 runs each way, non-interleaved, comparing two separate blocks of boots) read as 3281 ms median
with recording against 3196 ms without, about 85 ms (2-3%) of a roughly 3.2 s boot, but that gap
was run-to-run noise, not the cost of recording: a review pass (2026-09-23) re-measured with 8
interleaved ABBA pairs in one process after a warm-up boot, plus direct timing of the handler
itself, and found the handler (central-logger subscription plus, since the same-day channel fix
above, the per-mod-logger subscriptions and `VerifyFromMod`) costs 0.05 to 0.09 ms per boot (0.5 ms
on the cold first boot) over about 1450 `EntryAdded` calls, while the two boot-level medians came
out at 3048 ms with recording and 3076 ms without: no measurable difference, well inside this
machine's own ±100 ms run-to-run spread (3016-6878 ms across the original, non-interleaved runs).
One delegate call per logged entry at any level, not per tick or per asset, so the handler cost
scales with how much a boot actually logs; only `Warning` or above is ever formatted or matched.

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

- `src/Atlas/Internal/Hosting/ServerHost.cs`: the subscription in `BootServer`, mod-attribution
  resolution (`KnownModNames`) and the strict check in `FinishBoot`.
- `src/Atlas/Internal/Diagnostics/BootDiagnosticsLog.cs`: the pure recording/filtering core this
  feeds (also listed under 0005-pure-decision-core-thin-io-shell.md), including
  `ResolveModAttribution`.
- `src/Atlas/Internal/Diagnostics/BootDiagnosticsAllowlist.cs`: the strict-mode allow-rule filter.
- `src/Atlas/Api/AllowedBootDiagnostic.cs`: the allow-rule shape.
- `src/Atlas.XUnit/AtlasWorldAttribute.cs`: `StrictBootDiagnostics`, `ExcludeAssemblyMods`.
- `src/Atlas.XUnit/AtlasAllowBootDiagnosticAttribute.cs`: the allowlist declaration.
- `src/Atlas.XUnit/Internal/AttributeMapper.cs`: mapped onto `WorldOptions` alongside `SaveFile`.
