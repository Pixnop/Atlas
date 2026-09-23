# Boot diagnostics as assertions

Date: 2026-09-23
Status: measured and implemented (this pass)
Tracks: a Discord report (public) that a mod author checks skill-tree assets parse by booting
Atlas, but a failed load only ever reached server-main.log, never the test result
Game versions measured: 1.21.7, 1.22.3, 1.22.7 (see "What this pass did not verify" below)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[0002-one-live-host-per-process](../adr/0002-one-live-host-per-process.md)

## Motivation

Atlas boots a real embedded server. Everything the engine itself checks at boot (JSON syntax,
property types, recipe ingredients resolving to a real item) it already checks for a mod under
test, and it already logs when something is wrong. None of that reached a scenario: the entries
went to `server-main.log` and nowhere else, so a mod author's "does it boot clean" check could
only ever be "does it boot at all", and a broken asset silently kept working (the engine degrades
gracefully: it logs and moves on) until someone happened to read the log by hand.

## Method

A throwaway fixture mod, `tests/BootDiagnosticsFixtureMod` (content-only, no C#, modeled after
`samples/SampleMod`), ships three assets shaped exactly like the three ways a mod author breaks
one by mistake:

- `blocktypes/malformed.json`: a blocktype JSON with a missing closing brace (invalid JSON).
- `blocktypes/badproperty.json`: a well-formed blocktype JSON whose `resistance` is a string
  (`"very hard indeed"`) instead of a number.
- `recipes/grid/missingitem.json`: a well-formed grid recipe whose one ingredient is
  `game:doesnotexistatall`.

Booted with `ServerHost` directly (an E2E test, no xUnit adapter needed for a research spike) and
the raw `server-main.log` read back, the engine logged one or two entries per case (five in all),
always at `Error` or `Warning`, and boot always continued past all three to `RunGame`:

```
[Error] Syntax error in json file 'bootdiagfixture:blocktypes/malformed.json': Failed
  deserializing malformed.json: Unexpected end when reading token. Path ''.

[Error] Exception thrown while trying to parse json data of the type with code
  bootdiagfixture:bootdiagbadproperty, variant bootdiagfixture:bootdiagbadproperty. Will
  ignore most of the attributes. Exception:
[Error] Exception: Could not convert string to double: very hard indeed. Path 'resistance'.
  <raw stack trace lines follow, no level prefix, part of the same LogException call>

[Warning] Failed resolving crafting recipe ingredient with code game:doesnotexistatall in
  Grid recipe
[Error] Grid Recipe with output 'game:stone-granite' contains an ingredient that cannot be
  resolved: Item code game:doesnotexistatall
```

Never `Fatal`: the engine's asset/recipe loader treats every one of these as "skip this asset,
keep going" (the mod still loads and starts; the block/recipe in question is just absent or
half-populated). This is what made the original report possible in the first place: the boot
"worked", so nothing failed.

### The engine hook

`Vintagestory.API.Common.ILogger` (public mod API, `ICoreAPI.Logger`) has an `EntryAdded` event:

```csharp
public event LogEntryDelegate EntryAdded; // (EnumLogType type, string message, object[] args)
```

Three things measured about it directly in-process (a temporary probe in the same E2E spike,
deleted after measurement) that shaped the design:

1. **`ServerMain.Logger` and `ICoreServerAPI.Logger` are reference-equal** (`ServerLogger`, on
   this engine): one subscription sees everything either name would.
2. **Every mod also gets its own `Mod.Logger`** (`Vintagestory.Common.ModLogger`, a distinct
   instance per mod), but a message logged through it still reaches
   `ServerMain.Logger.EntryAdded`, prefixed with `"[modid] "`. So a single subscription on
   `ServerMain.Logger` sees the engine's own boot-time logging (our three cases, all unprefixed)
   and anything a mod logs through its own logger, at any point in the host's life.
3. **`EntryAdded` fires with the raw format string, not the formatted message.** A probe call
   `mod.Logger.Warning("PROBE {0}", "hello")` was observed by `EntryAdded` as
   `"PROBE {0}"` verbatim, `args` carrying `["hello"]` separately. A consumer that wants the text
   `server-main.log` shows has to format it itself.

The subscription point: `ServerMain.Logger` is created in `ServerHost.BootServer` (via
`ConfigureEngineStatics`), before `PreLaunch()`/`Launch()`, the same place `ServerMain.Logger`
was already being assigned directly (`src/Atlas/Internal/Hosting/ServerHost.cs`). Subscribing
right after that assignment means the recorder is live before any asset is ever loaded; the
bridge mod's `StartServerSide` (the earliest point a scenario's own code could reach
`ICoreServerAPI.Logger`) runs long after asset loading is done, so there is no other way to see
these three cases from inside a scenario.

### What this pass did not verify

`ILogger` and `EntryAdded` are used directly, not through `EngineCompat`
(0003-engine-compatibility-by-shape-probing.md): they are public mod-facing API (the same surface
any shipping mod already calls `api.Logger.Warning(...)` through), not an internal field or
method whose shape is known to move between supported versions the way `Entity.Pos` or the exit
lifecycle do. `ServerMain.Logger` itself is already read and written directly, unguarded,
elsewhere in `ServerHost`; this only adds a subscription on the same static. The four
`BootDiagnosticsTests` were built and run against 1.21.7 and 1.22.7 as well as the 1.22.3 local
install, passing on all three with identical entry shapes; a decompile comparison also confirms
`ILogger.EntryAdded` and `LogEntryDelegate` are unchanged across the three. If a future engine
version changes the shape of `ILogger` itself, it is caught by ci.yml's newest-version lane and
the compat.yml sweep's watch above the floor (not the 1.21.7 floor lane by itself), the same net
every other direct API call in this codebase relies on.

The asset-path heuristic (below) is over free-form message text, not a structured field the
engine provides, so it is approximate by construction; see the `ponytail:` comment at its
definition for the upgrade path if that ever needs to be exact.

## Environmental noise (CI follow-up, 2026-09-23)

PR #143's E2E lane 1.21.7 shard C, run 35798661573, failed
`BootDiagnostics_Should_StayEmpty_When_NoModShipsBrokenAssets` on
`[Warning] Server overloaded. A tick took 791ms to complete.`: a clean boot with no mod under
test, on a loaded CI runner. `ServerMain` logs this itself whenever a tick takes too long; it says
nothing about any mod's assets.

To find every distinct message shape a clean boot can produce (not just this one), every
`server-main.log` kept by ci.yml's scratch sweep (`if: failure()`, so only a run with at least one
red class uploads it) was pulled and grepped for `[Warning]`/`[Error]`/`[Fatal]` lines between the
first log line and `Singleplayer Server now running!` (the ready line this engine actually emits;
"Dedicated Server now running" never appears, single-player embedded boot only):

- Run 35798661573 (this failure), `e2e-scratch-logs-1.21.7-C`: 41 scratch directories, one per
  test class the shard ran (the sweep keeps every class's directory when the job is red, not only
  the failing class's).
- Run 34033803349 (2026-09-06, pre-dates this feature but not the engine boot sequence),
  `e2e-scratch-logs-1.22.7`: 97 scratch directories, from an earlier CI shape (one shard, no
  fixture mod anywhere in the suite yet).

138 clean-boot windows in all. Every `Warning`/`Error`/`Fatal` line found across both, classified:

| Message shape | Level | Count | Class |
| --- | --- | --- | --- |
| `Server overloaded. A tick took {N}ms to complete.` | Warning | 9 (4 + 5) | Environmental: the machine, not the mod. Logged by `ServerMain` itself whenever a tick runs long; `N` was 791-2609 across the samples, uncorrelated with any mod or asset. |
| `Syntax error in json file '{modid}:{path}': ...` | Error | 3 | Real content diagnostic: `BootDiagnosticsFixtureMod`'s intentional malformed JSON, present only in classes that stage that fixture. |
| `Exception thrown while trying to parse json data of the type with code {modid}:{code}, variant {modid}:{code}. Will ignore most of the attributes. Exception:` + `Exception: Could not convert string to double: ... Path '...'.` | Error | 3 + 3 | Real content diagnostic: the fixture's wrong-typed property. |
| `Failed resolving crafting recipe ingredient with code {item} in Grid recipe` + `Grid Recipe with output Item code {item} contains an ingredient that cannot be resolved: Item code {item}` | Warning + Error | 3 + 3 | Real content diagnostic: the fixture's missing recipe ingredient. |

No third shape turned up: across 138 samples there is no "engine noise a clean vanilla boot always
emits" distinct from the tick warning above and the fixture's own intentional entries. The
`BootDiagnostics_Should_StayEmpty_When_NoModShipsBrokenAssets` guard (`Assert.Empty` on a boot
with no mod under test) already encoded that expectation; what was missing was that the tick
warning can appear on that same clean boot when the runner is loaded, which is exactly what
happened here.

These 138 samples all ran the vanilla engine. Atlas also targets the Stratum fork (see
`StratumParity`), whose `ServerMain` logs the same tick-overload warning from the same
`Logger.Warning` call at the same threshold, but worded "Server may be overloaded. A tick took
{0}ms to complete." (confirmed by decompiling `ServerMain` from a Stratum install); the rule below
accepts both wordings so a slow Stratum machine cannot produce the same false positive.

**The rule.** `BootDiagnosticsLog.Add` discards a message matching
`^Server (may be )?overloaded\. A tick took \d+ms to complete\.$` before it is ever recorded, the
same way it already discards anything below `Warning`. It is dropped rather than recorded with a
flag that `BootDiagnostics` readers and strict mode would then have to ignore: that would add a
field to the public `BootDiagnosticEntry` record for a distinction nothing outside this one filter
needs to make, and every reader of `World.BootDiagnostics` (a scenario, `FinishBoot`'s strict
check, this test) would have to remember to apply it. Dropping it at the source keeps
`BootDiagnosticEntry` and `IWorldSession.BootDiagnostics` exactly as simple as before this fix:
their meaning is unchanged, and `StrictBootDiagnostics` no longer fails because a machine was busy
during boot.
`tests/Atlas.Pure.Tests/Diagnostics/BootDiagnosticsLogTests.cs` pins this with the real measured
message shapes (791ms, 2609ms, the format-string form, the Stratum wording, and a near-miss that
must still be kept).

## Design

**Recording.** A pure core, `Atlas.Internal.Diagnostics.BootDiagnosticsLog`, takes one raw
`(EnumLogType, rawMessage, args)` triple at a time and decides whether to keep it:

- Keep only `Warning`, `Error` and `Fatal`; every other level (`Chat`, `Event`, `StoryEvent`,
  `Build`, `VerboseDebug`, `Debug`, `Notification`, `Audit`) is noise for this purpose and
  discarded immediately.
- Format the message with its args (`string.Format`, falling back to the raw message when the
  placeholders and args disagree, since a malformed entry is still worth keeping over losing it).
- Discard the formatted message if it matches `EnvironmentalNoise`
  (`^Server (may be )?overloaded\. A tick took \d+ms to complete\.$`, see "Environmental noise"
  above): the tick-overload warning, vanilla or Stratum wording, measured to be about the machine,
  not the mod under test.
- Split a `"[modid] "` prefix off into a `Source` (`"engine"` when there is none), matching the
  measured `ModLogger` shape.
- Pick out a best-effort `AssetPath`: the first `domain:token`-shaped substring in the message
  (`[a-z][a-z0-9_]*:[A-Za-z0-9_\-./]+`), or `null` when there is none. This is right for a message
  naming one asset and only approximate for one naming several: the grid-recipe `Error` line
  above mentions both the recipe's output (`game:stone-granite`) and its missing ingredient
  (`game:doesnotexistatall`); the heuristic picks whichever is mentioned first (the output, in
  that case). Marked `ponytail:` at its definition: acceptable because the companion `Warning`
  line from the same failure already names the actual missing ingredient, so the information is
  never lost, only sometimes on a different entry than expected.

Never unsubscribed, never cleared: the recorder lives for the whole host lifetime (one host per
scenario class, ADR-0002), so it keeps recording through the scenario too. That was a simplicity
choice, not a requirement with its own toggle: "stop recording at world-ready" would need extra
state for no benefit anyone asked for, since nothing about a scenario's own warnings piling up in
the same list is wrong; a scenario that only cares about the boot window can still filter by
reading the list right at the start of its body.

**Exposure.** `IWorldSession.BootDiagnostics` (`IReadOnlyList<BootDiagnosticEntry>`), matching the
existing read-only-list-of-records shape (`IClientObservations`). `BootDiagnosticEntry` is a
public record: `Level` (`EnumLogType`, reusing the engine's own enum; `IWorldSession` already
returns engine types directly, e.g. `Block`, `Entity`, `EnumReplaceMode`), `Source`, `Message`,
`AssetPath`.

**Strict mode.** `[AtlasWorld(StrictBootDiagnostics = true)]`, not an assembly-level option or a
new attribute: it is boot-scoped exactly like the world it configures (one host per class, the
same lifecycle every other `AtlasWorldAttribute` member already governs), and the closest existing
precedent, `AtlasScenarioAttribute.StrictIsolation`, lives at the same granularity as what it
makes strict (`RollbackWorld` is class-scoped too, via the shared host). An assembly-level switch
would force every scenario class in a project into the same choice, which is wrong for a project
whose classes stage different mods-under-test with different maturity. Off by default (default
behavior unchanged, the explicit requirement): a suite that never sets it keeps whatever entries
the engine logs, readable but non-fatal. When it is set and `BootDiagnosticsLog` holds anything by
the time the world is ready (the same checkpoint `FinishBoot` already uses to publish the host and
release the boot waiter; the recorder is checked immediately before that point, so a strict
failure never lets a scenario see a host that is about to die), `ServerHost` throws
`AtlasBootDiagnosticsException` with every offending entry listed, one per line. This is an
exception, not an outcome object
(0006-outcome-objects-on-expected-degrade-paths.md): 0006 reserves outcome objects for paths that
have a designed fallback (rollback degrades to a recycle); there is no fallback for "the assets
you shipped do not parse", so this follows 0006's own rule for the unexpected and fails the same
way `StrictIsolation` and a bridge-startup failure already do: a boot-time exception the xUnit
adapter turns into a failed class.

## What changed

- `Atlas.Api.BootDiagnosticEntry` (new public record): `Level`, `Source`, `Message`, `AssetPath`.
- `Atlas.Api.AtlasBootDiagnosticsException` (new public exception).
- `Atlas.Api.IWorldSession.BootDiagnostics` (new property) /
  `Atlas.Api.WorldOptions.StrictBootDiagnostics` (new property, default `false`).
- `Atlas.XUnit.AtlasWorldAttribute.StrictBootDiagnostics` (new property, default `false`), mapped
  by `AttributeMapper` exactly like `SaveFile` and the rest.
- `Atlas.Internal.Diagnostics.BootDiagnosticsLog` (new, internal, pure): the recording/filtering
  core, unit-tested without an embedded server.
- `ServerHost`: subscribes `BootDiagnosticsLog.Add` to `ServerMain.Logger.EntryAdded` right after
  the logger is created (`BootServer`), and fails the boot before publishing the host when strict
  mode is on and the recorder is non-empty (`FinishBoot`).
- `tests/BootDiagnosticsFixtureMod`: the fixture from the "Method" section above, kept (not
  actually thrown away) as the E2E fixture `BootDiagnosticsTests` stages.
- `samples/Sample.Scenarios/BootDiagnosticsScenarios.cs`: the realistic usage from the bug report
  itself: assert no `Error`-or-above entries for a mod that is supposed to boot clean.
- `Atlas.Internal.Diagnostics.BootDiagnosticsLog.Add` (2026-09-23 CI follow-up): discards the
  `Server overloaded` tick warning, the one message shape measured to be environmental rather
  than about the mod under test; see "Environmental noise" above.

## Consequences

- A mod author's "does it boot clean" check is now a real assertion, not a manual log read.
- Nothing about a suite that never touches this feature changes: the recorder always runs (the
  subscription itself is unconditional, and it costs one delegate call per logged entry of any
  level, since the level filter runs inside `Add`, not before it, immeasurable next to booting a
  server), but nothing reads or acts on it unless a scenario calls `World.BootDiagnostics` or a
  class opts into `StrictBootDiagnostics`.
- The `AssetPath` heuristic can point at the wrong one of several assets a single message names;
  `Message` always has the full text regardless, so nothing is hidden, only sometimes
  mis-highlighted.
- `ILogger`/`EntryAdded` being used directly instead of through `EngineCompat` is a bet that public
  mod API is stable enough not to need shape probing, consistent with how the rest of
  `WorldSession`/`ClientObservations` already call the engine's public surface directly; unlike
  `EngineCompat`'s own members it has no dedicated probe, relying instead on ci.yml's
  newest-version lane and the compat.yml sweep to catch a future break.
- `StrictBootDiagnostics` cannot fail because a CI runner (or any other machine) was busy during
  boot: the one measured environmental message shape is filtered before it is ever recorded, so a
  slow tick is invisible to both the default recorder and strict mode, the same as if the engine
  had never logged it.

## Field feedback on 0.14.0-rc.1 (2026-09-23)

Two real consumers ran 0.14.0-rc.1 against their own suites (Nimbus, a server mod, 30/30
scenarios; StratumParity, 20 scenarios on vanilla and Stratum) with no regression, and reported
four gaps back. All four are addressed in this pass, on the same branch this spec already covers.

### 1. Source was a guess

Nimbus logs through `api.Logger` with its own hand-written `"[Nimbus] "` prefix, so its entries
were labelled `Source = "Nimbus"` (not even its mod id, `"nimbusserver"`), and its unprefixed
lines were labelled `"engine"`. Both were guesses: nothing before this pass ever checked a
bracketed prefix, or the absence of one, against a real mod.

**What was measured.** `Vintagestory.Common.ModContainer`, `ModLogger` and `LoggerBase` were
decompiled (`ilspycmd`) from the 1.21.7 and 1.22.7 installs at `~/dev/.vs-compat`. `ModLogger` is
byte-identical between the two; `LoggerBase` differs only by `[StringSyntax]` attributes added for
nullable-aware analyzers (same `Log`/`LogImpl` order on both). `ModContainer` itself is not
byte-identical (711 decompiled lines on 1.21.7, 776 on 1.22.7), but every member this section
relies on (the constructor's `Logger` assignment, the load-time error call sites) is unchanged
between them; only the surrounding, unrelated members of the class differ.

- `ModContainer`'s constructor runs `base.Logger = new ModLogger(parentLogger, this)`: every mod
  container gets its own `ModLogger`, holding a reference back to that exact container
  (`ModLogger.Mod`), before a single asset or line of mod code loads.
- `ModLogger.LogImpl` forwards every call into `Parent.Log(logType, "[" + (Mod.Info?.ModID ??
  Mod.FileName) + "] " + message, args)` - the parent being the shared central logger
  (`ServerMain.Logger`, the same instance this spec's "Method" section already established
  `EntryAdded` is subscribed to). So `Mod.Logger.Warning(...)` (a mod's own call) and the engine's
  own per-mod load-time logging (`ModContainer.LoadModInfo`/`LoadAssembly`/`InstantiateModSystems`
  all log through `base.Logger`, i.e. this same `ModLogger` - literally "the mod container the
  engine names in a load error") both reach the central logger the same, single way: prefixed,
  attributable.
- `LoggerBase.Log` runs `LogImpl(...)` then `EntryAdded?.Invoke(...)` on the SAME instance,
  strictly in that order. For a `ModLogger` call this means the prefixed echo always reaches the
  central logger's `EntryAdded` (and gets recorded) before anything else happens; nothing else
  about attribution depends on catching that echo in flight.
- Asset loading (`AssetManager.GetMany<T>(ILogger logger, ...)`, the source of this spec's three
  fixture cases) takes a plain `ILogger` and, at every call site that matters here, is handed
  `api.Logger` - the CENTRAL logger directly, never a `ModContainer`'s own `ModLogger`. A broken
  asset's message is never mod-attributed at the source, no matter whose domain the asset belongs
  to (confirmed: the fixture's own `bootdiagfixture:` assets still produce unprefixed entries).

So a bracketed prefix on an entry means one of two genuinely different things - "the engine
verifiably routed this through a specific mod's own logger" or "some code decided to type a
bracket at the front of a plain `api.Logger` call" - and nothing on the wire tells them apart.
`Mod.Logger` is the only channel where "verifiably" applies.

**The fix, first attempt.** `BootDiagnosticsLog` stopped trusting a bracketed prefix as `Source`
outright. It kept parsing one out (widened to accept any content up to `]`, not just mod-id
characters, so a file-name fallback like `"MyMod.dll"` parses too, the old regex's documented
gap), filed it as `SourceHint`, never `Source`, and only promoted it once
`ServerHost.FinishBoot` called `_bootDiagnostics.ResolveModAttribution(names)` with every mod id
and file name the engine actually loaded (`ICoreServerAPI.ModLoader.Mods`, read once the bridge
hands back the API, the mod list being final by then). That call cross-checked every hint recorded
so far and upgraded a match to a verified `Source`, and the same check ran on every entry recorded
afterwards. Cross-referencing against the real mod list, rather than subscribing per `ModLogger`
live, was meant to sidestep a genuine ordering problem: a mod's own early load-time errors (during
`ModLoader.CollectMods`/`LoadModInfos`) fire before Atlas's own bridge mod, itself mod code loaded
in the same pass, could ever subscribe to anything mod-specific.

That reasoning held for the ordering problem, but a name match checked after the fact is still
only a name match: a mod can write its own hand-written bracket with its real mod id spelled
correctly (a review finding, 2026-09-23, concrete case below), and `ResolveModAttribution` would
then verify it exactly as if it had come through `Mod.Logger`, which is precisely the guess this
feature exists to stop making.

```
[mymod] patch target missing         # logged by an ADDON mod, through the shared api.Logger,
                                      # with a hand-written "[mymod] " bracket copying the other
                                      # mod's real id
```

With only the assembly-mods and StartPre fix in place, that entry would resolve to
`Source == "mymod"`, attributed to the wrong mod, with no signal anywhere that it was never
verified by anything but a string match.

**The fix.** `BootDiagnosticsLog` no longer verifies `Source` from a name match at all, except in
the one place nothing else can reach: `Atlas.Bridge.BridgeModSystem` now overrides `ExecuteOrder`
to the lowest possible value and hooks `StartPre` (called for every mod, in ascending
`ExecuteOrder`, before any mod's own `Start`/`StartServerSide`), publishing
`ICoreAPI.ModLoader.Mods` (already fully populated, since `ModLoader.LoadMods` already ran) back
to `ServerHost` through the same AppDomain-slot rendezvous the API handoff already uses.
`ServerHost.SubscribeModLoggers` then subscribes directly to every loaded mod's own
`Mod.Logger.EntryAdded`. `ModLogger.LogImpl` forwards every call into the central logger first
(what `BootDiagnosticsLog.Add` observes and records, still unverified) and only fires the mod's
own `EntryAdded` afterwards, synchronously, on the same thread (`LoggerBase.Log`'s own order,
decompile-confirmed on 1.21.7 and 1.22.7, see "What was measured" above); `VerifyFromMod` uses
that guaranteed ordering, through a `[ThreadStatic]` pending-entry marker, to mark the entry
`Add` just built as verified for that exact mod: channel evidence, not a name.
`ResolveModAttribution`'s name match still exists, but `BootDiagnosticsLog.BeginModLoggerVerification`
(called once `SubscribeModLoggers` has wired every subscription) closes it off for anything
recorded from that point on, leaving it eligible only for an entry recorded before any such
subscription could exist at all: the engine's own per-mod-container load error (a missing
`modinfo.json`, a failed assembly load), logged while the mod list itself is still being built,
before the bridge mod's own `StartPre` has even run.

`Source` is the literal `"unknown"` (not `"engine"`) whenever no mod verifies: an unprefixed
asset-loading message and a mod's own unverified bracket convention are equally unattributable at
the source, and claiming `"engine"` for either would be exactly the guess this fix removes.
`IModLoader.Mods` only lists enabled mods, so a mod that fails to load entirely (crashes before
`Enabled` ever becomes true) is left out of the known-name set, and its own early errors stay
`"unknown"` rather than verified. Documented as a real, narrow limitation, not silently patched
over: it costs nothing that used to work (those errors were `"engine"`, an equally wrong guess,
before this pass), and the overwhelmingly common case a mod author cares about, a mod's own
`Mod.Logger` call once that mod has loaded, verifies correctly and by channel.

Not routed through `EngineCompat`, for the same reason `ILogger`/`EntryAdded` already were not:
`ICoreServerAPI.ModLoader.Mods`, `Mod.Info`, `Mod.FileName`, `Mod.Logger` and `ModSystem.StartPre`/
`ExecuteOrder` are public mod API, unchanged on 1.21.7 and 1.22.7 (measured above), not one of
`EngineCompat`'s internal-shape targets.

### 2. Strict mode was all-or-nothing

Nimbus warns by design when it boots unconfigured; that warning alone made `StrictBootDiagnostics`
permanently unusable for any class staging it, with no way to say "expected, ignore this one."

`[AtlasAllowBootDiagnostic(pattern, Level = ..., Source = ...)]` (`Atlas.XUnit`, `AttributeUsage`
on `Assembly` and `Class`, `AllowMultiple = true`, named after the existing
`AtlasDataFiles`/`AtlasMods` pair) declares one exemption: a regex against `Message` (required),
and an optional `Level` and `Source` to narrow it further. `AttributeMapper.Map` collects
assembly-level rules then class-level ones into `WorldOptions.AllowedBootDiagnostics`, and
`ServerHost.FinishBoot` runs `BootDiagnosticsAllowlist.Filter` on the strict snapshot before
counting offenders - a pure, unit-tested filter
(`Atlas.Internal.Diagnostics.BootDiagnosticsAllowlist`) that compiles each pattern with the same
bounded match timeout `BootDiagnosticsLog`'s own regexes use, and fails a `RegexMatchTimeoutException`
CLOSED (the entry still counts as offending: a rule that cannot be evaluated must never silently
approve a real diagnostic). `WorldSession.BootDiagnostics` is untouched by the allowlist: it always
reads `BootDiagnosticsLog.Snapshot()` directly, so a matched entry still shows up there, exactly as
the field report asked ("BootDiagnostics still lists them (so tests can see what was allowed)") -
only `FinishBoot`'s local strict-check variable is filtered.

`Level`, on the attribute, is a plain string (an `EnumLogType` member name, e.g. `"Warning"`), not
`EnumLogType` itself: `Atlas.XUnit` ships with no reference to the game assembly at all (every
existing attribute in that package is primitive-typed for the same reason), and `Atlas.Api`'s
`AllowedBootDiagnostic.Level` follows suit for symmetry. `BootDiagnosticsAllowlist.Compile` parses
it (`Enum.TryParse<EnumLogType>`) where the game assembly IS reachable, and an unrecognized name
fails the boot with every valid name listed, the same fail-fast contract `AttributeMapper`'s regex
validation already has.

### 3. No vanilla baseline class

Nimbus had to stand up a whole separate standalone server, outside Atlas, just to see what a clean
engine logs, because `AtlasWorldAttribute.Mods` only ever APPENDS to the assembly-wide
`[AtlasMods(...)]` set - there was no way to boot one class without it.

`AtlasWorldAttribute.ExcludeAssemblyMods` (off by default) skips both assembly-wide sources
(`[AtlasMods(...)]`'s own paths AND the MSBuild-generated `<AtlasMod>true</AtlasMod>` manifest -
the attribute's own docs already call the manifest "an alternative to listing paths [in
`AtlasModsAttribute`] by hand", so an opt-out of one that left the other in force would not be a
real opt-out) while leaving the class's own `Mods` untouched; `AttributeMapper.Map` gates the
first two sources on the flag and always appends the third.

The consequence bullet the task asked to check - "a class with a different mod set must not reuse
a host booted with the assembly mods" - does not need a `HostRegistry` change, and the reason is
worth recording rather than assumed: `HostRegistry.GetOrCreateAsync` keys the live host by
`_ownerClass == testClass` (`Type` reference equality), and disposes-and-recreates
(`CreateAsync`, which re-runs `AttributeMapper.Map(testClass)` from scratch) on every owner
change. A different class is a different `Type`, so it ALWAYS gets a freshly-booted host from its
own, freshly-computed recipe; the same class always resolves to the same recipe (attributes do not
change at runtime), so reusing that class's cached host is safe regardless of
`ExcludeAssemblyMods`. Two classes with different mod sets - opted out or not - can never
observe one another's host. This was true before this pass too; `ExcludeAssemblyMods` simply gives
a class a mod set worth being different about.

### 4. Cost not stated

Measured on this machine (AMD Ryzen 9 9900X, Linux, VS 1.22.3, single-threaded embedded boot, no
mod under test): 7 back-to-back `ServerHost.StartAsync()` runs with the recorder's subscription
and `ResolveModAttribution` call present, then 7 more with both temporarily removed (a throwaway
local edit, reverted after measuring, the same probe-and-delete method the original "Method"
section above used).

| | Runs (ms, sorted) | Median |
| --- | --- | --- |
| With recording | 3071, 3140, 3221, 3281, 3384, 4142, 6878 | 3281 ms |
| Without recording | 3016, 3086, 3187, 3196, 3232, 4075, 6861 | 3196 ms |

Median delta: about 85 ms on a ~3.2 s boot, roughly 2-3%. Both runs share a late outlier
(6878/6861 ms, most likely disk-cache/JIT warmup on the process's first boot in that block), which
in hindsight was the tell that the two blocks were not comparable: with only 7 runs each, one
slow boot lands its whole block above the other's median regardless of recording. This read as
recording costing a small, real amount. It does not: a review pass (2026-09-23) re-measured with 8
interleaved ABBA pairs in one process (recording, no recording, recording, ...) after a warm-up
boot, so both conditions share the same JIT/disk-cache state instead of one block warming up
before the other, plus direct timing of the `EntryAdded` handler itself. The handler costs 0.05 to
0.09 ms per boot (0.5 ms on the cold first boot) over about 1450 `EntryAdded` calls, and the two
interleaved boot-level medians came out at 3048 ms with recording against 3076 ms without: no
measurable difference, well inside this machine's own run-to-run spread (roughly ±100 ms at the
median in the interleaved runs too).

The earlier reading also overstated what the cost would scale with: `EntryAdded` fires once per
logged entry at every level (`Chat` through `Fatal`), not only `Warning` or above, since the level
filter runs inside `BootDiagnosticsLog.Add` rather than before the subscription; a clean boot logs
roughly 1450 such calls, and only a handful clear the `Warning` bound and are actually formatted,
matched or recorded. "One delegate call per Warning-or-above entry" undercounted the calls the
handler itself sees by about two orders of magnitude, even though the corrected conclusion (this
is not measurable against a normal boot's own cost) still holds either way.

The figure, corrected, is stated in `BootDiagnosticsLog`'s own XML docs, the wiki page and ADR
0008 rather than left silent, as asked. It was not measured on every supported engine version or
hardware class, so treat "not measurable against run-to-run noise on this machine" as the honest
finding, not a guarantee that holds everywhere.

### What changed (this pass)

- `Atlas.Api.BootDiagnosticEntry.SourceHint` (new property): the unverified bracket parse, only
  populated when `Source` is `"unknown"`.
- `Atlas.Api.BootDiagnosticEntry.Source`: `"engine"` retired; `"unknown"` is the new
  no-verified-mod sentinel (a behavior change, acceptable pre-1.0: see CHANGELOG's `[Unreleased]`).
- `Atlas.Api.AllowedBootDiagnostic` (new public record), `Atlas.Api.WorldOptions.AllowedBootDiagnostics`
  (new property, default empty).
- `Atlas.XUnit.AtlasAllowBootDiagnosticAttribute` (new), `AtlasWorldAttribute.ExcludeAssemblyMods`
  (new property, default `false`), both mapped by `AttributeMapper`.
- `Atlas.Internal.Diagnostics.BootDiagnosticsLog`: `ResolveModAttribution` (new), the bracket parse
  widened and demoted to a hint.
- `Atlas.Internal.Diagnostics.BootDiagnosticsAllowlist` (new, internal, pure): the allow-rule
  filter.
- `ServerHost.FinishBoot`: calls `ResolveModAttribution` before the strict check, and filters the
  strict snapshot through `BootDiagnosticsAllowlist`.
- `tests/BootDiagnosticsFixtureMod`: now a code mod too (`type: code` in `modinfo.json`, its own
  `BootDiagnosticsFixtureModSystem`, built with its output redirected outside the folder that gets
  staged - see the csproj comment - so the mod's own dll ends up next to `modinfo.json`/`assets/`
  without the mod loader also scanning stray `bin`/`obj` artifacts as candidate mod files) that
  logs one warning through `api.Logger` with a hand-written bracket that does NOT match its real
  mod id, and one through its own `Mod.Logger`.

## Source files

- `src/Atlas/Internal/Diagnostics/BootDiagnosticsLog.cs`: the recording/filtering core, including
  `ResolveModAttribution`.
- `src/Atlas/Internal/Diagnostics/BootDiagnosticsAllowlist.cs`: the strict-mode allow-rule filter.
- `src/Atlas/Api/BootDiagnosticEntry.cs`, `AtlasBootDiagnosticsException.cs`,
  `AllowedBootDiagnostic.cs`: the public shapes.
- `src/Atlas/Internal/Hosting/ServerHost.cs`: the subscription (`BootServer`), mod-attribution
  resolution and the strict check (`FinishBoot`, `KnownModNames`, `DescribeStrictFailure`).
- `src/Atlas/Internal/Hosting/WorldSession.cs`: `BootDiagnostics`, threaded from the host.
- `src/Atlas.XUnit/AtlasWorldAttribute.cs`, `AtlasAllowBootDiagnosticAttribute.cs`,
  `Internal/AttributeMapper.cs`: the declaration surface.
- `tests/Atlas.Pure.Tests/Diagnostics/BootDiagnosticsLogTests.cs`,
  `BootDiagnosticsAllowlistTests.cs`, `tests/Atlas.Pure.Tests/XUnit/AttributeMappingTests.cs`: the
  pure tests.
- `tests/BootDiagnosticsFixtureMod/`, `tests/Atlas.Engine.Tests/BootDiagnosticsTests.cs`: the E2E
  fixture and tests.
- `samples/Sample.Scenarios/BootDiagnosticsScenarios.cs`: the sample scenario.
