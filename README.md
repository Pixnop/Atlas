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
  own connection and inventory. `ITestPlayer.IsConnected` reports when the server dropped
  one (kick, ban), so mods that kick players are testable end to end.
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
  recycle, for scenarios that only pollute world state; `RestartWorld = true` restarts the
  server for real and carries the world over, so scenarios can assert on what survives a
  genuine save/load round trip (SaveGame moddata, manifests, whatever a mod writes for
  reload). Isolation is observable: a rollback that degrades to a full recycle reports the
  classified reason in the scenario's own test output, every class prints an isolation
  summary, and `StrictIsolation = true` turns a silent degrade into a failure.

## Quickstart

Requirements: a Vintage Story 1.22.x install, the `VINTAGE_STORY` environment variable
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
    <PackageReference Include="Pixnop.Atlas.XUnit" Version="0.7.0" />
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

4. Run it:

```sh
dotnet test
```

Atlas boots a fresh headless server per test class (superflat world, creative playstyle,
fixed seed by default), pumps it on a dedicated game thread, runs your scenario on that
thread, then tears it down.

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
non-zero. Full flag reference on the wiki's
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
| 1.21.7 | Incompatible | Build fails: the server exit lifecycle Atlas hooks (`EnumExitMode`, `GameExitState`, `ServerMain.exitState`) does not exist before 1.22 |
| 1.20.12 | Incompatible | Build fails: same missing exit lifecycle API as 1.21.7 |
| 1.19.8 | Incompatible | Build fails: same as above, plus `ServerMain.PreLaunch` and the `ServerProgramArgs` boot signature differ |
| 1.18.15 | Incompatible | Build fails: same as above, plus `Vintagestory.API.Common.Func` collides with `System.Func` |

Each row is the latest patch of its minor available on the stable CDN; 1.18.0 through
1.18.7 predate the game's .NET migration and ship under a different server archive
entirely. The supported floor is therefore Vintage Story 1.22.0. The table reflects the
sweep run of 2026-07-03; the sweep re-runs weekly and can be triggered manually with
`gh workflow run compat.yml` or from the Actions tab.

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
- World rollback is stage 1: dimension 0 only, no test players in a rollback-enabled class,
  and neither mod globals nor in-memory map chunk state (height maps, map moddata) are
  rolled back; when a rollback cannot be trusted, Atlas falls back to the full host recycle
  and reports the degrade in the scenario's test output (or fails the scenario, with
  `StrictIsolation = true`). See the wiki's
  [Writing Scenarios](https://github.com/Pixnop/Atlas/wiki/Writing-Scenarios)
  page for the honest boundary list.
- Parallelism is per scenario class and multi-process (`atlas run --parallel`): scenarios
  within a class still run sequentially, and `dotnet test` itself remains sequential
  (`DisableTestParallelization` stays required).

## License

MIT. See [LICENSE](LICENSE).
