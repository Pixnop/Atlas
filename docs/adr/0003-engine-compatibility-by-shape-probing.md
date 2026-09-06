# 0003. Engine-version compatibility by shape probing in one shim

Status: accepted (`docs/specs/2026-07-12-pre-122-compat.md`).

## Context

One Atlas binary has to boot against several Vintage Story versions, and the engine's
internals move between them. The exit lifecycle is the whole compile-level gap below 1.22:
1.22 has `ServerMain.exitState` of type `GameExitState` and a four-argument `Stop`, while
1.21 and 1.20 have `ServerMain.exit` of type `GameExit` and a three-argument `Stop`.
`Entity.Pos` and `ServerPos` are fields before 1.22 and properties since. `GameVersion`
constants are `const`, so the compiler bakes the build machine's values into Atlas's IL and
the server rejects the resulting network version on every join.

Two shapes of answer exist: switch on the version, or ask the loaded assembly what it has.
A version switch is a closed set, and Atlas is used against forks whose version strings do
not parse into that set at all.

## Decision

Every engine touchpoint whose shape varies lives in one class, `EngineCompat`, and is
resolved by probing the loaded types rather than by branching on a version number. Handles
resolve once per process behind `Lazy`, since the embedded engine cannot change mid-process,
and `ValidateAtBoot` forces all of them before any engine state is touched, so a drifted
engine fails at boot with the game version and the missing symbol named instead of dying
mid-scenario. A version below the supported floor is rejected up front; a version string
that does not parse, which is what a fork looks like, is let through and judged on its
members alone.

The source rule that keeps the single binary honest: outside `EngineCompat`, Atlas source
never mentions an engine type or member that does not exist on every supported version. The
1.21.7 CI lane exists to enforce it, because a violation compiles fine against the newest
install.

## Consequences

- Forks and unreleased builds work whenever their members do, without a code change.
- Drift is a boot-time failure with a named symbol, never a mid-run mystery.
- The compat surface is only as good as its matrix: shape probing catches layout drift,
  never behavioral change, so the version matrix stays the authority on behavior.
- New engine touchpoints have to be pushed through `EngineCompat` deliberately. The
  reflection lookups in `WorldSnapshot.Create` are the one place that still resolves its own
  handles.

## Source files

- `src/Atlas/Internal/Bootstrap/EngineCompat.cs`: the shim and its source rule at `:15`-`:37`,
  the `Lazy` handles at `:39`-`:102`, `ValidateAtBoot` at `:184`, `ResolveExitStateField` at
  `:362`, `StopBinding.Resolve` at `:409`, the floor check that lets forks through at
  `:326`-`:332`.
- `src/Atlas/Internal/Hosting/ServerHost.cs:374`: `ValidateAtBoot` called before any engine
  state is touched.
- `.github/workflows/ci.yml`, `compat.yml`: the per-push matrix and the weekly sweep.
