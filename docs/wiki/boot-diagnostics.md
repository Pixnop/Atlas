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
warnings show up here too, not only boot-time ones. Each entry has:

- `Level`: `EnumLogType.Warning`, `.Error` or `.Fatal`. Nothing below `Warning` is kept.
- `Source`: `"engine"` for the engine's own central logger, or a mod's id when the entry came
  through that mod's own `Mod.Logger`.
- `Message`: the formatted text (format arguments substituted), with the `"[modid] "` prefix
  stripped and no timestamp or level, so it is close to but not exactly the `server-main.log`
  line.
- `AssetPath`: the asset the message appears to be about (e.g. `"mymod:blocktypes/broken.json"`
  or `"mymod:brokenblock"`), when the message names one; `null` otherwise. Best effort: a message
  naming more than one asset (a recipe naming both its output and its missing ingredient) only
  gets the first one, so lean on `Message` for the full text when `AssetPath` alone is not enough.

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
world becoming ready. Off by default, so an existing class's behavior does not change until it
opts in.
