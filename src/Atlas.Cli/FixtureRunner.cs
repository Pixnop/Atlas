namespace Atlas.Cli;

/// <summary>Implements `atlas fixture`: runs exactly one builder scenario in-process (the same
/// mechanics as `atlas run --filter`), then harvests the world save the host's graceful
/// teardown persisted and copies it to --out. The builder is an ordinary [AtlasScenario] whose
/// side effect is building the world; the produced file is what
/// <c>[AtlasWorld(SaveFile = "...")]</c> boots against.</summary>
internal static class FixtureRunner
{
    /// <summary>Builds the fixture: select, validate, run, harvest, copy.</summary>
    /// <param name="arguments">The parsed `fixture` arguments.</param>
    /// <param name="output">Destination for progress and per-scenario lines.</param>
    /// <param name="error">Destination for usage and failure diagnostics.</param>
    /// <returns>The process exit code: 0 with the fixture written, 1 when the builder failed or
    /// left no save (no fixture is written then), 2 on usage errors (zero or several scenario
    /// matches, or an existing --out without --force).</returns>
    public static int Run(FixtureArguments arguments, TextWriter output, TextWriter error)
    {
        var filter = new ScenarioFilter(arguments.Scenario);
        IReadOnlyList<DiscoveredScenario> matches = ScenarioDiscovery.Find(arguments.AssemblyPath, filter);
        if (FixtureScenarioSelection.Validate(matches, arguments.Scenario) is { } selectionError)
        {
            error.WriteLine($"atlas: {selectionError}");
            return 2;
        }

        string outPath = Path.GetFullPath(arguments.OutPath);
        if (FixtureOutput.Validate(outPath, arguments.Force, File.Exists) is { } outputError)
        {
            error.WriteLine($"atlas: {outputError}");
            return 2;
        }

        output.WriteLine($"Building world fixture from scenario: {matches[0].DisplayName}");
        int runExitCode = ScenarioRunner.Run(arguments.AssemblyPath, filter, output);

        // Harvest before judging the outcome: disposing the builder's host is what makes the
        // engine persist the save, and a failed builder must not leave a live server behind
        // either. Only a passed run turns the harvested save into the fixture.
        string? savePath = FixtureHarvest.ShutDownAndHarvestSavePath(out string? harvestError);
        if (runExitCode != 0)
        {
            error.WriteLine("atlas: the builder scenario failed; no fixture was written.");
            return 1;
        }

        if (harvestError is not null)
        {
            error.WriteLine($"atlas: {harvestError}");
            return 1;
        }

        if (savePath is null || !File.Exists(savePath))
        {
            error.WriteLine(
                "atlas: the builder scenario passed but left no world save to harvest "
                + "(is it an [AtlasScenario] on a class deriving from AtlasScenarioBase?).");
            return 1;
        }

        if (Path.GetDirectoryName(outPath) is { Length: > 0 } parent)
        {
            Directory.CreateDirectory(parent);
        }

        File.Copy(savePath, outPath, overwrite: arguments.Force);
        output.WriteLine($"Fixture written: {outPath}");
        output.WriteLine("Boot it with [AtlasWorld(SaveFile = \"path/to/fixture.vcdbs\")] on a scenario class.");
        return 0;
    }
}
