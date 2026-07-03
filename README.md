# Atlas

Atlas is an in-process integration-test harness for Vintage Story mods. It boots a real,
headless Vintage Story server inside your `dotnet test` process, drives it tick by tick, and
lets you write deterministic scenarios in plain C# with xUnit. No client, no window, no
manual server setup: `dotnet test` boots the world, runs your scenarios against the live
game API, and tears it down.

Atlas is generic: any Vintage Story mod is testable. It has no dependency on any particular
mod.

[![CI](https://github.com/Pixnop/Atlas/actions/workflows/ci.yml/badge.svg)](https://github.com/Pixnop/Atlas/actions/workflows/ci.yml)

## Quickstart

Requirements: a Vintage Story 1.22.x install, the `VINTAGE_STORY` environment variable
pointing at its binaries folder (the directory containing `VintagestoryAPI.dll`), and
.NET 10.

1. Create an xUnit test project and reference Atlas.XUnit:

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
  </ItemGroup>

  <ItemGroup>
    <!-- VintagestoryAPI is needed to compile game types (BlockPos) used in scenario bodies. -->
    <Reference Include="VintagestoryAPI">
      <HintPath>$(VINTAGE_STORY)\VintagestoryAPI.dll</HintPath>
    </Reference>
    <ProjectReference Include="path/to/Atlas.XUnit/Atlas.XUnit.csproj" />
  </ItemGroup>

  <!-- Overwrites the test SDK's transitive Newtonsoft.Json with the game's own copy.
       Without it, E2E runs fail with a cryptic MissingMethodException at runtime. -->
  <Import Project="path/to/build/Atlas.E2E.targets" />

</Project>
```

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

## Documentation

- [Architecture](docs/wiki/architecture.md): engine, adapter and bridge layers, the
  game-thread pump.
- [Getting started](docs/wiki/getting-started.md): the quickstart above, expanded, plus
  troubleshooting.
- [Writing scenarios](docs/wiki/writing-scenarios.md): attribute reference, time model,
  world isolation, the `Api` escape hatch.
- [Design spec](docs/specs/2026-07-02-atlas-design.md) and
  [feasibility spike](docs/feasibility-spike.md) for the engineering rationale.

## Known limitations (v1)

- `EntitiesIn` only queries dimension 0.
- Vintage Story 1.22.2 occasionally throws a `NullReferenceException` from
  `ServerSystemMonitor.Dispose()` during shutdown. Atlas swallows it; it is a known flake in
  the embedded server, not a symptom of a broken test run. See
  [getting-started.md](docs/wiki/getting-started.md) for details.
- No parallel scenario execution, no world snapshot/rollback, no CLI facade yet. Tracked as
  GitHub issues (`future:` prefix) rather than left as silent gaps.

## License

MIT. See [LICENSE](LICENSE).
