# Atlas samples

Runnable versions of everything the README describes. They are part of CI: the sample lane runs
them on every game version in the matrix, so they double as the compatibility proof for the
public authoring surface.

```sh
VINTAGE_STORY=/path/to/vintagestory dotnet test samples/Sample.Scenarios
```

- `SampleMod` is a content-only folder mod (JSON assets, no code). It is staged through the
  relative path in `Sample.Scenarios/AssemblyInfo.cs`, which is the manual form of mod staging.
  Having no `.cs` file is the point: it shows a folder mod boots without a build step.
- `SampleConfigMod` is a code mod. It is staged by the `AtlasMod=true` metadata on its
  `ProjectReference` in `Sample.Scenarios.csproj`, so MSBuild passes its built dll to Atlas and
  no path is written by hand.
- `Sample.Scenarios` holds one file per feature: `MarkerScenarios` for blocks and commands,
  `ParameterizedScenarios` for `[AtlasTheory]`, `ConfigScenarios` for `[AtlasDataFiles]` and the
  test-player chat surface, `IsolationScenarios` for `RollbackWorld`.
