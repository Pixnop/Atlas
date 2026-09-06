# 0001. Boot a real ServerMain in-process on a dedicated game thread

Status: accepted (design spec `docs/specs/2026-07-02-atlas-design.md`, evidence in
`docs/feasibility-spike.md`).

## Context

A mod test needs the game's real block registry, world generation, entity simulation and
event bus. Mocking that surface reproduces the mod author's assumptions rather than the
engine's behavior. Two alternatives were available: drive a separate server process over
its network protocol, which turns every assertion into IPC and every failure into a log
scrape, or embed the engine.

Vintage Story's server can be embedded, but only under its own threading rule: whichever
thread calls `Launch()` becomes `RuntimeEnv.ServerMainThreadId`, and every later
`Process()` call must come from that same thread.

## Decision

Atlas boots a real `ServerMain` inside the test process, with `isDedicatedServer: false`
so no socket is ever opened, and dedicates one thread per host to the engine's loop. That
thread does the whole lifecycle: preflight, environment redirection, staging, `PreLaunch()`,
`Launch()`, then a pump that alternates `Process()` with draining the scheduler queue until
shutdown or a fatal error. Scenario bodies are posted onto that queue, so they run on the
game thread with race-free access to the game API and no locking of their own.

## Consequences

- Scenario code touches live engine objects, and a failing assertion is a normal test
  failure with a normal stack.
- Everything that follows from the engine's process-wide statics follows from here: one
  live host per process (ADR 0002), a bridge to reach `ICoreServerAPI` (ADR 0004).
- The engine's own crash paths become Atlas's problem. An engine-initiated stop turns
  `Process()` into a silent sleep loop, so the pump watches the engine's `stopped` flag and
  reports a stop Atlas did not request as a host crash.
- Boot is the dominant cost of a scenario, which is what the isolation modes of ADR 0006
  exist to avoid paying.

## Source files

- `src/Atlas/Internal/Hosting/ServerHost.cs`: the game thread at `:180`, `GameThreadMain`
  at `:362`, the pump at `:443`-`:452`, its engine-stop watch at `:463`, `BootServer` at `:601` with
  `isDedicatedServer: false` at `:625` and `PreLaunch()`/`Launch()` at `:632`-`:633`.
- `src/Atlas/Internal/Scheduling/GameThreadScheduler.cs:6`: the `SynchronizationContext`
  the pump drains, so awaits inside a scenario return to the game thread.
- `src/Atlas/Internal/Hosting/EngineStopDetection.cs`: the engine-initiated-stop rule.
- `src/Atlas/Internal/Bootstrap/GameEnvironment.cs`: base-directory redirect and assembly
  resolution against the install.
