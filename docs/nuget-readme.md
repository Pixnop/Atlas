# Atlas

Atlas is an in-process integration-test harness for Vintage Story mods. It boots a real,
headless Vintage Story server inside your `dotnet test` process, drives it tick by tick, and
lets you write deterministic scenarios in plain C# with xUnit. No client, no window, no manual
server setup. What the server sends a test player (block highlights, particles, mod-channel
packets, chat) is captured and decoded as a real client would decode it, still with no client
process.

Atlas is generic. Any Vintage Story mod is testable, and the harness depends on no particular
mod.

## Requirements

- .NET 10.
- A Vintage Story install at 1.21.0 or newer (1.20.x works best-effort).
- `VINTAGE_STORY` pointing at that install's binaries folder, the directory holding
  `VintagestoryAPI.dll`.

## Install

```sh
dotnet add package Pixnop.Atlas.XUnit
```

`Pixnop.Atlas.XUnit` is the package to reference from a test project; it brings in
`Pixnop.Atlas` (the engine) and `Pixnop.Atlas.Bridge` (the mod assembly the harness stages into
the game) on its own. `Pixnop.Atlas.Cli` is a separate .NET tool that runs the same scenarios
from a compiled assembly without VSTest.

The Newtonsoft.Json shadowing fix that end-to-end runs need ships inside the package as a
`buildTransitive` target, so it applies automatically. There is no `Import` to add by hand.

## A first scenario

Two assembly-level declarations set the harness up:

```csharp
using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Resolved relative to the test assembly's output directory.
[assembly: AtlasMods("relative/path/to/your/mod")]
```

Then a scenario. This one places a vanilla block, so it runs without any mod at all:

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

`dotnet test` boots the world, runs the scenario against the live game API, and tears it down.

## More

- [Full README](https://github.com/Pixnop/Atlas): feature tour, compatibility table, design notes.
- [Wiki](https://github.com/Pixnop/Atlas/wiki): getting started, writing scenarios, the CLI,
  troubleshooting.
- [Changelog](https://github.com/Pixnop/Atlas/blob/main/CHANGELOG.md).
- [Mod DB page](https://mods.vintagestory.at/atlas): follow releases and leave feedback.

MIT licensed.
