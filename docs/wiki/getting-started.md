# Getting started

## Requirements

- A Vintage Story 1.22.x install (server or client, either works: Atlas only needs
  `VintagestoryAPI.dll` and the game's own libraries).
- The `VINTAGE_STORY` environment variable set to the folder containing
  `VintagestoryAPI.dll`.
- .NET 10 SDK.

## 1. Create the test project

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
    <Reference Include="VintagestoryAPI">
      <HintPath>$(VINTAGE_STORY)\VintagestoryAPI.dll</HintPath>
    </Reference>
    <ProjectReference Include="path/to/Atlas.XUnit/Atlas.XUnit.csproj" />
  </ItemGroup>

  <Import Project="path/to/build/Atlas.E2E.targets" />

</Project>
```

Each piece matters:

- The `Reference` to `VintagestoryAPI.dll` is required because game types (`BlockPos` and
  friends) appear directly in `IWorldSession` method signatures used in your scenario
  bodies, so the test project must compile against them.
- The `Atlas.E2E.targets` import copies the game's own `Newtonsoft.Json.dll` over whatever
  version the test SDK pulled in transitively. Skipping it does not fail the build; it fails
  at test run time instead, with a confusing `MissingMethodException`.

## 2. Declare assembly-level settings

```csharp
using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AtlasMods("relative/path/to/your/mod")]
```

- `DisableTestParallelization` is required: Atlas can only host one live server per process,
  so scenario classes cannot run concurrently.
- `AtlasModsAttribute` paths (folder, `.zip`, or `.dll`) are resolved relative to the test
  assembly's own output directory, not the source tree. If your mod lives at
  `samples/SampleMod` and your test assembly builds to
  `samples/Sample.Scenarios/bin/Debug/net10.0/`, the path is
  `"../../../../SampleMod"` (up through `net10.0`, the configuration folder, `bin`, and the
  project folder, then down into the mod).

## 3. Write a scenario

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

## 4. Run it

```sh
dotnet test
```

See [writing-scenarios.md](writing-scenarios.md) for the full attribute reference and
authoring rules.

## Troubleshooting

### `AtlasSetupException`

Thrown when Atlas cannot prepare the test environment. Common causes:

- `VINTAGE_STORY` is unset, or does not point at a folder containing
  `VintagestoryAPI.dll`. Atlas needs this to locate the game's binaries and libraries for
  assembly resolution.
- A path in `[assembly: AtlasMods(...)]` (or `[AtlasWorld(Mods = ...)]`) does not resolve to
  an existing folder, `.zip`, or `.dll` relative to the test assembly's output directory.
- The Vintage Story mod loader rejected a staged mod (bad `modinfo.json`, dependency
  resolution failure). The exception message includes the mod loader's own report.

### `MissingMethodException` at test run time (often inside Newtonsoft.Json or ServerConfig code)

This almost always means `<Import Project=".../build/Atlas.E2E.targets" />` is missing from
the test project, or `VINTAGE_STORY` was unset at build time. The test SDK pulls in its own
Newtonsoft.Json, which shadows the game's own build in the output directory; without the
targets import, the game's compiled code runs against the wrong version and fails with a
cryptic `MissingMethodException` instead of a clear setup error. Add the import, rebuild,
and re-run.

### `VINTAGE_STORY` unset

Both the build (compiling against `VintagestoryAPI.dll`) and the test run (booting the
embedded server) need this environment variable. Set it once in your shell profile or CI
environment to the folder containing `VintagestoryAPI.dll`, for example:

```sh
export VINTAGE_STORY=/opt/vintagestory
```

### `NullReferenceException` from `ServerSystemMonitor.Dispose()` during shutdown

Vintage Story 1.22.2 has a known shutdown flake in the embedded server: it occasionally
throws a `NullReferenceException` from `ServerSystemMonitor.Dispose()` while tearing down.
Atlas catches and swallows this specific failure at teardown; it does not affect scenario
results and is not something a test author needs to work around. It is tracked upstream as
a `bug:` issue for visibility.

### Scenario hangs instead of failing

`await World.Until(...)` timeouts are tick-based (`timeoutTicks`), which only elapses while
the server is actually ticking. If the server itself is stuck, the scenario's `TimeoutMs`
watchdog (default 60 seconds of wall-clock time, set via `[AtlasScenario(TimeoutMs = ...)]`)
is what actually fails the test and marks the class host dead. If a run hangs for
substantially longer than that, check for a `ConfigureAwait(false)` in the scenario body,
see [writing-scenarios.md](writing-scenarios.md).
