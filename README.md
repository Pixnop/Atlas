<p align="center">
  <img src="docs/assets/logo.svg" width="220" alt="Atlas logo: a kneeling titan carrying a cube of layered earth" />
</p>

# Atlas

Atlas is an in-process integration-test harness for Vintage Story mods. It boots a real,
headless Vintage Story server inside your `dotnet test` process, drives it tick by tick, and
lets you write deterministic scenarios in plain C# with xUnit. No client, no window, no
manual server setup: `dotnet test` boots the world, runs your scenarios against the live
game API, and tears it down.

Atlas is generic: any Vintage Story mod is testable. It has no dependency on any particular
mod.

[![CI](https://github.com/Pixnop/Atlas/actions/workflows/ci.yml/badge.svg)](https://github.com/Pixnop/Atlas/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Pixnop.Atlas.XUnit.svg)](https://www.nuget.org/packages/Pixnop.Atlas.XUnit)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Atlas&metric=alert_status)](https://sonarcloud.io/project/overview?id=Pixnop_Atlas)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

## What scenarios can do

- Query and mutate the live world: blocks, block entities, entities, commands (with results
  a scenario can assert on), all on the game thread, deterministically.
- Join headless test players: real, world-present players, several per world, each with its
  own connection and inventory. Joined players complete the engine's own join sequence and
  count as Playing for server systems, so anything that filters or counts Playing players
  (proximity queries, playing-count broadcasts, natural spawning) sees them exactly like
  real clients. `ITestPlayer.IsConnected` reports when the server dropped one (kick, ban),
  so mods that kick players are testable end to end.
- Seed data files before boot: `[AtlasDataFiles]` copies config fixtures into the embedded
  server's data path before it launches, so mods that read their config once in
  `StartServerSide` boot configured.
- Boot against a prebuilt world save: `[AtlasWorld(SaveFile = "fixtures/myworld.vcdbs")]`
  loads a fixture world instead of generating one; every test class gets its own pristine
  copy, the fixture is never written to. The `atlas fixture` command builds the `.vcdbs`
  from an ordinary builder scenario, and `IWorldSession.PlaceSchematic` stamps a single
  prebuilt structure (a worldedit `.json` export) into the running world in one line.
- Pick the world isolation each scenario needs, per scenario: `FreshWorld = true` recycles
  the host for a brand-new world (strongest isolation, one full boot); `RollbackWorld =
  true` restores the same world in place without a reboot, roughly 25x faster than a
  recycle, and the rollback is universal: blocks, entities, savegame data, joined test
  players (position, inventories, stats, per-player moddata) and every mini-dimension all
  return to the captured baseline, with the `atlas:rollback:captured` /
  `atlas:rollback:restored` event-bus hooks keeping mods' own SaveGame-keyed in-memory
  state in sync (VintagestoryAPI types only); `RestartWorld = true` restarts the
  server for real and carries the world over, so scenarios can assert on what survives a
  genuine save/load round trip (SaveGame moddata, manifests, whatever a mod writes for
  reload). Isolation is observable: a rollback that degrades to a full recycle reports the
  classified reason and cost in the scenario's own test output, a restart reports its
  measured cost the same way (it is paid outside the scenario's own duration), every class
  prints an isolation summary with the accumulated costs (also aggregated by
  `atlas run --parallel` and attached to its TRX), and `StrictIsolation = true` turns a
  silent degrade into a failure.
- Parameterize a scenario: `[AtlasTheory]` with `[InlineData]`/`[MemberData]` runs one
  scenario per data row, xUnit-style: same settings as `[AtlasScenario]`, applied per row,
  with the row's arguments in the test's display name. Contributed by Seggr, the project's
  first external contribution.

## Quickstart

Requirements: a Vintage Story install at 1.21.0 or newer (1.20.x works best-effort, see
[Compatibility](#compatibility)), the `VINTAGE_STORY` environment variable
pointing at its binaries folder (the directory containing `VintagestoryAPI.dll`), and
.NET 10.

1. Create an xUnit test project and reference
   [Pixnop.Atlas.XUnit](https://www.nuget.org/packages/Pixnop.Atlas.XUnit):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Pixnop.Atlas.XUnit" Version="0.9.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- VintagestoryAPI is needed to compile game types (BlockPos) used in scenario bodies. -->
    <Reference Include="VintagestoryAPI">
      <HintPath>$(VINTAGE_STORY)\VintagestoryAPI.dll</HintPath>
    </Reference>
  </ItemGroup>

</Project>
```

The Newtonsoft.Json shadowing fix (see the wiki's
[Troubleshooting](https://github.com/Pixnop/Atlas/wiki/Troubleshooting) page) ships inside the
package as a `buildTransitive` target, so it applies automatically. No `<Import>` needed.

<details>
<summary>Building from source instead</summary>

```xml
<ItemGroup>
  <Reference Include="VintagestoryAPI">
    <HintPath>$(VINTAGE_STORY)\VintagestoryAPI.dll</HintPath>
  </Reference>
  <ProjectReference Include="path/to/Atlas.XUnit/Atlas.XUnit.csproj" />
</ItemGroup>

<!-- Overwrites the test SDK's transitive Newtonsoft.Json with the game's own copy.
     Without it, E2E runs fail with a cryptic MissingMethodException at runtime. -->
<Import Project="path/to/build/Atlas.E2E.targets" />
```

</details>

2. Add the two assembly-level declarations Atlas needs:

```csharp
using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Resolved relative to the test assembly's output directory.
[assembly: AtlasMods("relative/path/to/your/mod")]
```

3. Write a scenario. This one uses a vanilla block, so no mod is required to try Atlas out:

```csharp
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.MathTools;
using Xunit;

public class MarkerScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public async Task Chest_Should_BePlaceable_When_WorldIsReady()
    {
        BlockPos pos = World.Spawn.Offset(1, 1, 0);
        World.SetBlock("game:chest-east", pos);
        await World.Ticks(5);
        Assert.Equal("game:chest-east", World.BlockAt(pos).Code.ToString());
    }
}
```

Same scenario, several inputs? `[AtlasTheory]` is the theory-style counterpart: each data
row runs as its own scenario, with the row's values in its display name and rows passing or
failing independently:

```csharp
[AtlasTheory]
[InlineData("game:chest-east")]
[InlineData("game:chest-west")]
public async Task Chest_Should_BePlaceable_When_WorldIsReady(string chestCode)
{
    BlockPos pos = World.Spawn.Offset(1, 1, 0);
    World.SetBlock(chestCode, pos);
    await World.Ticks(5);
    Assert.Equal(chestCode, World.BlockAt(pos).Code.ToString());
}
```

4. Run it:

```sh
dotnet test
```

Atlas boots a fresh headless server per test class (superflat world, creative playstyle,
fixed seed by default), pumps it on a dedicated game thread, runs your scenario on that
thread, then tears it down.

Each embedded server works in its own scratch data directory (world save, server logs,
staged mods) under the system temp path. A class that ends green has its scratch deleted
at teardown; any failure, crash or abnormal exit keeps it, because the server's own
`server-main.log` in there is the post-mortem trail Atlas's failure messages point at.
Set `ATLAS_KEEP_SCRATCH=1` to keep every scratch directory, green ones included, when
debugging.

## The atlas CLI

For fast local iteration without VSTest, the `atlas` dotnet tool (package
`Pixnop.Atlas.Cli`) runs a compiled scenario assembly directly:

```sh
atlas run bin/Debug/net10.0/MyMod.Scenarios.dll            # run everything
atlas run bin/Debug/net10.0/MyMod.Scenarios.dll --filter Chest
atlas run bin/Debug/net10.0/MyMod.Scenarios.dll --list     # discover only, no server boot
atlas run bin/Debug/net10.0/MyMod.Scenarios.dll --parallel # one worker process per class
atlas fixture bin/Debug/net10.0/MyMod.Scenarios.dll \
      --scenario BuildsCastleWorld --out fixtures/castle.vcdbs   # author a world fixture
atlas diff vanilla.trx fork.trx                            # compare two runs, no server boot
atlas --version                                            # print the tool version, no boot
```

Scenarios execute in-process and sequentially, exactly like `dotnet test` would (same
embedded server, same `VINTAGE_STORY` requirement), with per-scenario PASS/FAIL lines,
durations, a summary, and a non-zero exit code on any failure.

`--parallel [N]` distributes the scenario classes over N worker subprocesses (default:
half the cores, capped at the class count), streams results live, reports the measured
speedup, and can write one aggregated TRX report with `--trx <path>`; a crashed or wedged
worker fails its class, never shortens the test list. `--worker` is the machine-facing
counterpart: the same sequential run, reported as line-delimited JSON events on stdout for
the orchestrator or any other tool.

`atlas fixture` authors the prebuilt world save that
`[AtlasWorld(SaveFile = "fixtures/castle.vcdbs")]` boots against: it runs exactly one
builder scenario (an ordinary `[AtlasScenario]` whose side effect is building the world),
selected by the `--scenario` display-name substring, and after the scenario passes and the
host shuts down gracefully it copies the persisted world save to `--out` (refusing to
overwrite an existing file without `--force`). A failing builder writes nothing and exits
non-zero.

`atlas diff` makes differential testing first class: it compares two TRX reports (the ones
`--parallel --trx` or plain `dotnet test --logger trx` write) by test name and reports new
failures, fixed, vanished and new tests, still-failing ones, and notable duration shifts
(at least 2x and 500 ms apart), as a compact console listing or a versioned JSON document
with `--json`. Exit codes gate CI directly: 0 no regressions, 1 regressions (a new failure
or a vanished test), 2 unreadable input, so
`atlas diff vanilla.trx fork.trx` IS the parity gate. Contract in
[docs/specs/2026-07-14-diff-command.md](docs/specs/2026-07-14-diff-command.md).

Full flag reference on the wiki's
[CLI](https://github.com/Pixnop/Atlas/wiki/CLI) page; protocol contract in
[docs/specs/2026-07-06-worker-protocol.md](docs/specs/2026-07-06-worker-protocol.md).

## Compatibility

Empirical results from the version compatibility sweep
([compat.yml](.github/workflows/compat.yml)), which builds Atlas against each game
version's own dlls and runs the full E2E suite on a real embedded server:

| Version | Status | Notes |
|---------|--------|-------|
| 1.22.3 | Compatible | All E2E scenarios green, tested on every push |
| 1.22.2 | Compatible | All E2E scenarios green, tested on every push |
| 1.22.1 | Compatible | All E2E scenarios green, tested on every push |
| 1.22.0 | Compatible | All E2E scenarios green, tested on every push |
| 1.21.7 | Compatible | All E2E scenarios green through the runtime `EngineCompat` shim (exit lifecycle, version constants, client-state value), tested on every push |
| 1.20.12 | Compatible | All E2E scenarios green through the same shim (verified live 2026-07-13); swept weekly, not tested per push |
| 1.19.8 | Incompatible | The boot API itself changes shape (`ServerMain.PreLaunch`, `ServerProgramArgs`); Atlas refuses to boot with a clear setup error citing the floor |
| 1.18.15 | Incompatible | Same as 1.19.8, plus `Vintagestory.API.Common.Func` collides with `System.Func` at build time |

Each row is the latest patch of its minor available on the stable CDN; 1.18.0 through
1.18.7 predate the game's .NET migration and ship under a different server archive
entirely. The supported floor is Vintage Story 1.21.0, with 1.20.x compatible best-effort
(same measured surface, one weekly sweep lane instead of a per-push one). The pre-1.22
rows reflect the live verification of 2026-07-13 (full engine E2E suite plus samples on
1.21.7 and 1.20.12, measured in
[docs/specs/2026-07-12-pre-122-compat.md](docs/specs/2026-07-12-pre-122-compat.md)); the
sweep re-runs weekly and can be triggered manually with `gh workflow run compat.yml` or
from the Actions tab.

### Multi-install runs: no rebuild needed

Repointing `VINTAGE_STORY` at a different install (another game version, or a server fork
like Stratum) does not require rebuilding the test project. The test output's own
`VintagestoryAPI.dll` copy would win assembly probing and mix with the target install's
`VintagestoryLib` (the issue #49 trap: a cryptic `MissingFieldException` on same-version
forks, a raw `TypeLoadException` killing the test process across versions), so Atlas
auto-stages at launch: a module initializer, running at xUnit discovery before anything
can bind the copy, rewrites the test-output dll+pdb pair from the target install
(atomically, with a one-line stderr notice). When staging is genuinely impossible: the
stale copy already bound because engine types were JITted before any Atlas code ran, a
read-only test output, an install without its pdb: the boot fails fast with an
`AtlasSetupException` naming both file identities and the remedies (an already-bound copy
is still re-staged on disk, so a plain re-run recovers without a rebuild). The per-push
`prebuilt-cross-install` CI lane proves the path from one build: samples built once
against 1.22.3 run unmodified (`--no-build`) on 1.21.7, again on 1.21.7 (idempotence),
and back on 1.22.3, with byte-identity asserts on the staged copy.

Direction matters, because of the second file the game provides: the test output's
`Newtonsoft.Json.dll` is the BUILD-time install's game build (13.0.4 on 1.22.x, 13.0.3 on
1.21.x/1.20.x, same 13.0.0.0 assembly identity), and the VSTest host binds it at process
start for its own protocol, before any staging trigger can possibly run. Build against
the NEWEST engine you target and run everywhere older: the newer build is a superset and
the whole matrix runs green (measured: the full engine E2E suite built on 1.22.3 passes
105/105 on 1.21.7 and 1.20.12, plus the samples, without a rebuild). Running a
floor-built output on a NEWER engine is refused up front: the newer engine binds members
its own Newtonsoft build added (`JToken.WriteTo(JsonWriter)` on 1.22.3, which killed
every boot in measurement), so the boot preflight fails fast with both file versions and
the remedies named, and still re-stages the copy on disk, so a plain re-run (or a
rebuild) recovers.

## Community

- [Mod DB page](https://mods.vintagestory.at/atlas): follow releases and leave feedback.

## Documentation

The full documentation lives on the
[project wiki](https://github.com/Pixnop/Atlas/wiki):

- [Getting Started](https://github.com/Pixnop/Atlas/wiki/Getting-Started): the quickstart
  above, expanded, plus troubleshooting.
- [Writing Scenarios](https://github.com/Pixnop/Atlas/wiki/Writing-Scenarios): attribute
  reference, time model, the world-isolation trilogy (fresh, rollback, restart), world
  fixtures and schematics, data file seeding, dimensions, test players, command results,
  the `Api` escape hatch.
- [Mod Staging](https://github.com/Pixnop/Atlas/wiki/Mod-Staging): folder/zip/dll staging,
  `AtlasMods`, the MSBuild `AtlasMod` sugar.
- [CLI](https://github.com/Pixnop/Atlas/wiki/CLI): the `atlas run` reference, filtering
  and listing, worker mode and the JSONL protocol, multi-process `--parallel` execution,
  authoring world fixtures with `atlas fixture`.
- [Architecture](https://github.com/Pixnop/Atlas/wiki/Architecture): engine, adapter and
  bridge layers, the game-thread pump.
- [CI Recipes](https://github.com/Pixnop/Atlas/wiki/CI-Recipes): GitHub Actions recipe,
  version matrix, TRX output, parallel execution.
- [Compatibility](https://github.com/Pixnop/Atlas/wiki/Compatibility): supported Vintage
  Story versions, the weekly sweep.
- [Troubleshooting](https://github.com/Pixnop/Atlas/wiki/Troubleshooting): common exceptions
  and how to resolve them.
- [Roadmap](https://github.com/Pixnop/Atlas/wiki/Roadmap): open issues and what's next.

For engineering rationale rather than usage docs, see the in-repo
[design spec](docs/specs/2026-07-02-atlas-design.md) and
[feasibility spike](docs/feasibility-spike.md).

## Known limitations (v1)

- Vintage Story (confirmed on 1.22.2 and 1.22.3) occasionally throws a
  `NullReferenceException` from `ServerSystemMonitor.Dispose()` during shutdown. Atlas
  swallows it and logs the stack to stderr; it is an upstream engine bug
  ([VintageStory-Issues#9798](https://github.com/anegostudios/VintageStory-Issues/issues/9798)),
  not a symptom of a broken test run. See the wiki's
  [Troubleshooting](https://github.com/Pixnop/Atlas/wiki/Troubleshooting) page for details.
- World rollback covers every dimension since stage 3 (mini-dimension chunk columns
  round-trip through the snapshot, boot-time pregenerated ones included), but neither mod
  in-memory state nor in-memory map chunk state (height maps, map moddata) is rolled back on
  its own: mods whose state is keyed to SaveGame data resync it by subscribing to the
  `atlas:rollback:restored` event-bus hook (payload-versioned, VintagestoryAPI types only;
  fired after the SaveGame restore, before any chunk column reloads), mods that follow
  chunk/entity lifecycle events need nothing, and scenarios over mods with neither use
  `FreshWorld`. When a rollback cannot be trusted (a throwing hook handler included), Atlas
  falls back to the full host recycle and reports the degrade in the scenario's test output
  (or fails the scenario, with `StrictIsolation = true`). Joined test players ARE rolled
  back since stage 2 (position, inventories, watched attributes, per-player moddata; players
  joined after the snapshot are removed and their names freed for a rejoin). See the wiki's
  [Writing Scenarios](https://github.com/Pixnop/Atlas/wiki/Writing-Scenarios)
  page for the honest boundary list.
- Parallelism is per scenario class and multi-process (`atlas run --parallel`): scenarios
  within a class still run sequentially, and `dotnet test` itself remains sequential
  (`DisableTestParallelization` stays required).
- Pre-1.22 engines (1.21.x, and 1.20.x best-effort) run through the runtime `EngineCompat`
  shim over the server exit lifecycle, validated at boot: an engine whose layout drifted
  fails fast with the game version and the missing symbol named, and 1.19.x or older is
  refused up front with a setup error citing the supported floor. The prebuilt-binary path
  (a test assembly and its NuGet Atlas.dll compiled against 1.22 running on a 1.21 install,
  or the reverse) is carried by the same shim plus the engine-assembly auto-staging
  preflight, and is verified per push by the `prebuilt-cross-install` CI lane; note that
  the samples it runs stick to the API surface both versions share, which is also the
  contract for your own cross-version scenarios (a scenario body calling a 1.22-only API
  member still fails on 1.21 with a `MissingMethodException`, staging cannot invent
  engine surface; enum VALUES are compile-time constants, so a scenario comparing against
  a member whose position shifted across versions, like `EnumClientState.Playing`, reads
  the wrong value on the other line, which is why Atlas's own join lifecycle resolves it
  from the loaded engine at run time; and members that changed between field and property,
  like `Entity.Pos`/`ServerPos` which became properties in 1.22, bind to one shape only,
  so scenario code should read spawned entities' positions through `SidedPos`, a property
  on every supported version, as Atlas itself does).

## License

MIT. See [LICENSE](LICENSE).
