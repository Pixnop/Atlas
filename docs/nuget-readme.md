# Atlas

Atlas is an in-process integration-test harness for Vintage Story mods. It boots a real,
headless Vintage Story server inside your `dotnet test` process, drives it tick by tick, and
lets you write deterministic scenarios in plain C# with xUnit. No client, no window, no manual
server setup. What the server sends a test player (block highlights, particles, mod-channel
packets, chat) is captured and decoded as a real client would decode it, still with no client
process. A scenario can also assert that the boot logged no warnings or errors
(`World.BootDiagnostics`) and measure what a window of ticks costs the server's game thread
(`World.MeasureTicks`).

Atlas is generic. Any Vintage Story mod is testable, and the harness depends on no particular
mod.

## Requirements

- .NET 10: the test project targets `net10.0`, even if your mod itself targets an older TFM
  (`net8.0` for Vintage Story 1.21).
- A Vintage Story install at 1.21.0 or newer (1.20.x works best-effort).
- `VINTAGE_STORY` pointing at the Vintage Story install directory (the one holding
  `VintagestoryLib.dll` next to `VintagestoryAPI.dll`).

## Install

```sh
dotnet new xunit -n MyMod.Tests
cd MyMod.Tests
dotnet add package Pixnop.Atlas.XUnit
```

Always pass `-n`: without it `dotnet new` names the project after the current folder, so
running it bare inside a folder called `Xunit` produces exactly the collision below.
It needs xUnit v2 2.9.3 or newer, which the template gives you by default; a template from an
older SDK can pin `xunit` lower and fail restore with `NU1107` once Atlas is added. xUnit v3
(the `xunit3` template, the `xunit.v3.*` packages) is not supported at all, and a project that
pulls it in fails the build with a clear Atlas error instead of a confusing compiler one, once
it targets `net10.0`; the `xunit3` template defaults to `net8.0`, where a different error shows
up first (see the wiki's Troubleshooting page linked below). Name the project anything except
the exact id of a package it references, case ignored (`Xunit`, `xunit`, `Pixnop.Atlas`,
`Pixnop.Atlas.XUnit`...): a project's identity to NuGet is its own name, so a project named
after a package it also depends on collides with itself and restore fails with `NU1108:
Cycle detected`. Keep Atlas in this test project, never in the mod's own csproj.

`Pixnop.Atlas.XUnit` is the package to reference from a test project; it brings in
`Pixnop.Atlas` (the engine), `Pixnop.Atlas.Bridge` (the mod assembly the harness stages into
the game) and `xunit.assert` (so `Assert` compiles with no separate `xunit` package needed) on
its own. `Pixnop.Atlas.Cli` is a separate .NET tool that runs the same scenarios from a
compiled assembly without VSTest.

Then add a reference to `VintagestoryAPI` (`$(VINTAGE_STORY)\VintagestoryAPI.dll`), and if
you're testing your own mod, reference its project too, never the other way around: a mod
project that references its own test project fails restore with a circular dependency
(`MSB4006`). The full csproj this produces is in the
[README Quickstart](https://github.com/Pixnop/Atlas#quickstart); troubleshooting for all of
the above is on the wiki's
[Troubleshooting](https://github.com/Pixnop/Atlas/wiki/Troubleshooting) page.

The Newtonsoft.Json shadowing fix that end-to-end runs need, and the xUnit v3 build guard,
both ship inside the package as a `buildTransitive` target, so they apply automatically.
There is no `Import` to add by hand.

## A first scenario

One assembly-level declaration is required, one is optional:

```csharp
using Atlas.XUnit;
using Xunit;

// Required: Atlas runs one embedded server per test class, so xUnit must not run classes
// in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Optional until you have a mod to stage; a path that does not resolve fails the boot.
// Resolved relative to the test assembly's OUTPUT directory, not the source tree: see
// https://github.com/Pixnop/Atlas/wiki/Mod-Staging.
// [assembly: AtlasMods("relative/path/to/your/mod")]
```

Then a scenario. This one places a vanilla block, so it runs without any mod at all:

```csharp
using Atlas.Api;
using Atlas.XUnit;
using System.Threading.Tasks;
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

`dotnet test` boots the world, runs the scenario against the live game API, and tears it down.

## More

- [Full README](https://github.com/Pixnop/Atlas): feature tour, compatibility table, design notes.
- [Wiki](https://github.com/Pixnop/Atlas/wiki): getting started, writing scenarios, the CLI,
  troubleshooting.
- [Changelog](https://github.com/Pixnop/Atlas/blob/main/CHANGELOG.md).
- [Mod DB page](https://mods.vintagestory.at/atlas): follow releases and leave feedback.

MIT licensed.
