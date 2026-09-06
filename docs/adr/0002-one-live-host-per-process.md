# 0002. One live host per process, a static registry keyed by scenario class

Status: accepted.

## Context

The embedded engine of ADR 0001 keeps its state in process-wide statics: `ServerMain.Logger`,
`GamePaths.DataPath`, `RuntimeEnv.ServerMainThreadId`, the global `TyronThreadPool.Inst`.
The feasibility spike proved that many server lifecycles can run one after another in the
same process. It did not make two concurrent servers in one process work, and nothing in the
engine suggests they could.

Ownership therefore needs an owner. xUnit's own answer would be a class fixture, but a
fixture is per test class with no knowledge of the other classes sharing the process, and it
cannot refuse to build a second one.

## Decision

A static `HostRegistry` owns the single live host of the process and records which scenario
class currently owns it. A scenario asks the registry for a host; the registry hands back
the cached one when the owner matches and the host is still usable, and otherwise disposes
the current host and boots a replacement from the requesting class's attributes. An
exclusive gate makes a concurrent second request fail loudly with the missing
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` named, rather than
producing two servers. Parallelism is therefore multi-process: `atlas run --parallel`
orchestrates one worker subprocess per scenario class.

Atlas uses no xUnit class fixture anywhere. The lifetime is the registry's, not xUnit's.

## Consequences

- Cross-class isolation is total and free: a class change disposes the previous host and
  boots a fresh server, world and scratch directory.
- The registry has to carry the failure vocabulary too. A class marked dead never gets
  another host, so its remaining scenarios fail fast instead of cascading into timeouts.
- A cached host that a later boot superseded is detected and rebooted rather than handed
  back: the boot's rendezvous reset severed its tick feed (ADR 0004), so every tick wait on
  it would hang.
- Registry decisions are tested by booting real servers, because the registry news up the
  sealed `ServerHost` directly. A factory seam would move them into the pure suite; that is
  an open question, not a decision.

## Source files

- `src/Atlas.XUnit/Internal/HostRegistry.cs`: the type and its rule at `:9`-`:13`, the
  process-exit disposal at `:22`, `GetOrCreateAsync` at `:38`, `MarkDead` at `:266`, the
  exclusive gate at `:290`, `CreateAsync` at `:319`.
- `src/Atlas/Internal/Hosting/ServerHost.cs:138`: `IsSuperseded`, the reuse test.
- `src/Atlas.XUnit/Internal/IsolationLedger.cs`, `ScratchLedger.cs`: per-class bookkeeping,
  static for the same reason.
- `src/Atlas.Cli/ParallelRunner.cs`: one worker subprocess per class.
