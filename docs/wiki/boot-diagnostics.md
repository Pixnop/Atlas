# Boot diagnostics (wiki section, not a standalone page)

The other files in this folder are retired redirects: the wiki itself lives on GitHub, not in
this repo (see `getting-started.md`/`architecture.md`/`writing-scenarios.md`). This file is not
a redirect: it is a new section ready to paste into the **Writing Scenarios** wiki page (a
natural home next to whatever it already says about `[AtlasWorld(...)]`), kept here because that
page's content is not.

---

## Boot diagnostics

Vintage Story's own asset loader already checks a mod's assets at boot (JSON syntax, property
types, recipe ingredients resolving to a real item) and logs when one is wrong. Normally that
only reaches `server-main.log`. Atlas records it instead, so "does my mod boot clean" is
something a scenario can assert:

```csharp
[AtlasScenario]
public Task Mod_Should_LoadWithNoErrors()
{
    Assert.DoesNotContain(World.BootDiagnostics, e => e.Level is EnumLogType.Error or EnumLogType.Fatal);
    return Task.CompletedTask;
}
```

`World.BootDiagnostics` is a read-only `IReadOnlyList<BootDiagnosticEntry>`: every engine log
entry at `Warning` level or above, oldest first, recorded from the start of the boot (before the
mod-under-test's own assets ever load) for as long as the class host is alive; scenario-time
warnings show up here too, not only boot-time ones. The engine's own "Server overloaded. A tick
took Nms to complete." warning is never recorded: it reports machine load, not a problem with any
mod. Each entry has:

- `Level`: `EnumLogType.Warning`, `.Error` or `.Fatal`. Nothing below `Warning` is kept.
- `Source`: the mod that produced the entry, VERIFIED against the mods the engine actually loaded
  (a call through that mod's own `Mod.Logger`, or a load-time error the engine itself logs about
  a specific mod container - both routed the same way), or the literal `"unknown"` when nothing
  verifies. A mod's own hand-written logging convention through the shared `api.Logger` (writing
  its own `"[MyMod] "` by hand, say) looks the same on the wire as a verified entry but is never
  one, so it stays `"unknown"` too - see `SourceHint` below for what was parsed either way. Never
  filter on `Source` expecting it to always name the right mod for an unprefixed message; it
  cannot, by construction, for anything the engine did not route through a specific mod's own
  logger.
- `SourceHint`: the `"[name] "`-shaped prefix a message started with, whether or not it verified
  to `Source`; `null` when there was no such prefix, and always `null` once `Source` is already
  verified. Useful for a human reading an `"unknown"` entry; never a trust signal (use `Source`,
  or `[AtlasAllowBootDiagnostic]`'s own `Source` filter, for that).
- `Message`: the formatted text (format arguments substituted), with a leading `"[name] "` prefix
  stripped whenever there was one (regardless of whether it verified), and no timestamp or level,
  so it is close to but not exactly the `server-main.log` line.
- `AssetPath`: the asset the message appears to be about (e.g. `"mymod:blocktypes/broken.json"`
  or `"mymod:brokenblock"`), when the message names one; `null` otherwise. Best effort: a message
  naming more than one asset (a recipe naming both its output and its missing ingredient) only
  gets the first one, so lean on `Message` for the full text when `AssetPath` alone is not enough.

Recording costs one delegate call per logged Warning-or-above entry; measured on one machine
(AMD Ryzen 9 9900X) at roughly 85 ms (2-3%) of a ~3.2 s boot with no mod under test - see ADR 0008
for the full figures. Scales with how much a boot logs, not with its size.

### Failing the boot outright

Reading the list is opt-in by nature: nothing fails unless a scenario asserts on it. To fail a
whole class's boot instead, with every offending entry named:

```csharp
[AtlasWorld(StrictBootDiagnostics = true)]
public class MyModScenarios : AtlasScenarioBase
{
    // any [AtlasScenario] here never runs if the boot logged a Warning-or-above entry
}
```

This throws `AtlasBootDiagnosticsException` at boot, before any scenario in the class runs, if
the engine logged anything at `Warning` level or above between the start of the boot and the
world becoming ready (the engine's tick-overload warning excepted, see above). Off by default,
so an existing class's behavior does not change until it opts in.

### Allowing a deliberate warning

A mod that warns on purpose (boots unconfigured with defaults, say) can still turn strict mode on
for its class: declare what to ignore instead of turning the whole check off.

```csharp
[AtlasWorld(StrictBootDiagnostics = true)]
[AtlasAllowBootDiagnostic("boots unconfigured, using defaults", Level = "Warning", Source = "mymod")]
public class MyModScenarios : AtlasScenarioBase
{
}
```

`MessagePattern` (the one required, positional argument) is a regular expression matched against
`Message`; `Level` (an `EnumLogType` member name, e.g. `"Warning"`) and `Source` narrow it further
and default to "any" when left out. Stackable (`[AtlasAllowBootDiagnostic(...)]` more than once,
at either the class or the assembly level - both apply, an assembly-wide allowance is never lost
at the class level) and additive only: it never hides anything from `World.BootDiagnostics`, only
from the strict check.

### Booting without the assembly's mods

A test assembly that declares `[assembly: AtlasMods("path/to/MyMod")]` stages that mod for every
scenario class in the assembly. One class can opt out and boot a vanilla baseline instead -
useful for telling "is this warning from my mod, or does a clean engine already log it" apart:

```csharp
[AtlasWorld(ExcludeAssemblyMods = true)]
public class VanillaBaselineScenarios : AtlasScenarioBase
{
    // boots with no mods at all, even though the assembly declares MyMod
}
```

Add `Mods = [...]` on the same attribute to boot a specific, narrower set instead of nothing: only
the assembly-wide set is excluded, never the class's own.
