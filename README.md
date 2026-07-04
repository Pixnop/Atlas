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
    <PackageReference Include="Pixnop.Atlas.XUnit" Version="0.1.0" />
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

## Compatibility

Empirical results from the version compatibility sweep
([compat.yml](.github/workflows/compat.yml)), which builds Atlas against each game
version's own dlls and runs the full E2E suite on a real embedded server:

| Version | Status | Notes |
|---------|--------|-------|
| 1.22.3 | Compatible | All E2E scenarios green |
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
  reference, time model, world isolation, dimensions, the `Api` escape hatch.
- [Mod Staging](https://github.com/Pixnop/Atlas/wiki/Mod-Staging): folder/zip/dll staging,
  `AtlasMods`, the MSBuild `AtlasMod` sugar.
- [Architecture](https://github.com/Pixnop/Atlas/wiki/Architecture): engine, adapter and
  bridge layers, the game-thread pump.
- [CI Recipes](https://github.com/Pixnop/Atlas/wiki/CI-Recipes): GitHub Actions recipe,
  version matrix, TRX output.
- [Compatibility](https://github.com/Pixnop/Atlas/wiki/Compatibility): supported Vintage
  Story versions, the weekly sweep.
- [Troubleshooting](https://github.com/Pixnop/Atlas/wiki/Troubleshooting): common exceptions
  and how to resolve them.
- [Roadmap](https://github.com/Pixnop/Atlas/wiki/Roadmap): open issues and what's next.

For engineering rationale rather than usage docs, see the in-repo
[design spec](docs/specs/2026-07-02-atlas-design.md) and
[feasibility spike](docs/feasibility-spike.md).

## Known limitations (v1)

- Vintage Story 1.22.2 occasionally throws a `NullReferenceException` from
  `ServerSystemMonitor.Dispose()` during shutdown. Atlas swallows it; it is a known flake in
  the embedded server, not a symptom of a broken test run. See the wiki's
  [Troubleshooting](https://github.com/Pixnop/Atlas/wiki/Troubleshooting) page for details.
- No parallel scenario execution, no world snapshot/rollback, no CLI facade yet. Tracked as
  GitHub issues (`future:` prefix) rather than left as silent gaps.

## License

MIT. See [LICENSE](LICENSE).
