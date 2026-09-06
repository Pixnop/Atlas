# 0004. Bridge rendezvous through AppDomain data slots

Status: accepted. Supersedes the shared-statics rendezvous described in
`docs/specs/2026-07-02-atlas-design.md`.

## Context

`ServerMain.api` is internal, so the only way to reach `ICoreServerAPI` from outside the
server is from inside it: a server-side `ModSystem` the game's own ModLoader loads.
`AtlasBridge` is that mod, and it also owns the tick listener that feeds `Ticks` and `Until`.

The bridge dll cannot be staged into the consumer's test output directory, because the
ModLoader would then scan that whole directory: test framework, mocking libraries and every
other dll in there. So Atlas stages `AtlasBridge.dll` alone into its own folder under the
scratch data path.

The original design assumed both sides would then observe the same assembly identity, and
had the mod hand the API to the engine layer through a shared static. They do not. The
ModLoader loads a copy of the file from the staged folder, and that copy is a distinct
assembly instance from the one Atlas references through its project reference. Statics
written on the mod side are never read on the Atlas side.

## Decision

The two sides meet through `AppDomain` data slots holding only framework-typed delegates.
Before each boot, `BridgeRendezvous.Reset` resets the rendezvous state and installs an
`Action<object>` under `atlas.bridge.publishApi` and an `Action` under `atlas.bridge.onTick`.
The mod's `StartServerSide` reads those two slots and never references `BridgeRendezvous`.
`ICoreServerAPI` crosses the boundary typed as `object`, which is safe because
`VintagestoryAPI.dll` is loaded once, from the install, and shared by both sides.

Data slots are identity-agnostic: `Action` and `Action<T>` come from the framework, so it
does not matter which assembly instance created the delegate.

## Consequences

- The handoff works regardless of how the ModLoader resolved the bridge assembly, with no
  IPC, no socket and no serialization.
- `Reset` is the boot's identity token. Because it replaces the `ApiReady` task, a host can
  tell that a later boot superseded it by comparing task references, which is exactly what
  `ServerHost.IsSuperseded` does and what ADR 0002's reuse check depends on.
- A superseded host's tick feed is severed, so it can only hang. That is a feature here and
  a hazard everywhere else; the registry has to check for it.
- The slot names are a string contract between two assemblies with no compiler between
  them. They are literals on both sides today.

## Source files

- `src/Atlas.Bridge/BridgeRendezvous.cs`: the mechanism and why at `:5`-`:13`, `Reset` and
  the two slots at `:28`-`:35`.
- `src/Atlas.Bridge/BridgeModSystem.cs`: the mod-side rule at `:14`-`:23`, the two slot
  reads at `:33`-`:41`.
- `src/Atlas/Internal/Hosting/ServerHost.cs`: bridge staged alone at `:396`-`:404`, `Reset`
  and the boot's identity capture at `:406`-`:407`, `IsSuperseded` at `:138`.
- `src/Atlas/Internal/Staging/ModStager.cs`: `StageBridge`.
