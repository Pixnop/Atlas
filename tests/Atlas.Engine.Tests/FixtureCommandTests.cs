using Atlas.Cli;

namespace Atlas.Engine.Tests;

/// <summary>Drives the fixture command's runner (`atlas fixture`) in-process, against the
/// scenario assemblies copied into this project's output. The happy path is the full round
/// trip the command exists for: build a fixture from the builder guinea pig
/// (Atlas.Fixture.Scenarios places a marker block near spawn), then boot a fresh
/// <see cref="ServerHost"/> from the produced file and assert the marker survived, mirroring
/// <see cref="WorldSaveTests"/>. The failure paths stay cheap: a failing builder (the
/// deliberately-failing Atlas.GuineaPig.Scenarios assembly) and a passing non-builder (a plain
/// xunit fact that never boots a host) must both write nothing and exit 1, and the pre-run
/// usage checks (zero or several matches, existing --out without --force) must exit 2 without
/// booting anything.</summary>
[Trait("Category", "E2E")]
public class FixtureCommandTests : IDisposable
{
    private const string BuilderScenario = "PlaceMarkerNearSpawn";
    private const string MarkerBlock = "game:soil-medium-normal";

    private readonly DirectoryInfo _outputRoot = Directory.CreateTempSubdirectory("atlas-fixturecmd-");

    private static string OutputDirectory => Path.GetDirectoryName(typeof(FixtureCommandTests).Assembly.Location)!;

    private static string BuilderDll => Path.Combine(OutputDirectory, "Atlas.Fixture.Scenarios.dll");

    private static string GuineaPigDll => Path.Combine(OutputDirectory, "Atlas.GuineaPig.Scenarios.dll");

    public void Dispose()
    {
        _outputRoot.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Fixture_Should_WriteASaveThatBootsWithTheMarker_When_BuilderScenarioPasses()
    {
        // The nested parent directory also proves the command creates missing parents.
        string fixture = Path.Combine(_outputRoot.FullName, "fixtures", "built-world.vcdbs");
        var console = new StringWriter();

        int exitCode = FixtureRunner.Run(
            new FixtureArguments(BuilderDll, BuilderScenario, fixture), console, console);

        string text = console.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Building world fixture from scenario", text);
        Assert.Contains($"Fixture written: {fixture}", text);
        Assert.True(File.Exists(fixture), "The fixture file was not written.");

        // The round trip: a fresh host booted from the fixture sees the builder's marker (same
        // assertion shape as WorldSaveTests). The offset pins the builder guinea pig's contract.
        await using var replayer = new ServerHost(
            new WorldOptions { SaveFile = fixture }, Array.Empty<string>(), AppContext.BaseDirectory);
        await replayer.StartAsync();
        await replayer.RunScenarioAsync(world =>
        {
            Assert.Equal(MarkerBlock, world.BlockAt(world.Spawn.Offset(3, 1, 0)).Code.ToString());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Fixture_Should_WriteNothingAndExitOne_When_BuilderScenarioFails()
    {
        string fixture = Path.Combine(_outputRoot.FullName, "never-written.vcdbs");
        var console = new StringWriter();

        // Exactly one match, and it fails its setup guard before any host is booted: the
        // cheapest way to prove a broken builder never produces a half-built fixture.
        int exitCode = FixtureRunner.Run(
            new FixtureArguments(GuineaPigDll, "ClassDoesNotDeriveFromBase", fixture), console, console);

        Assert.Equal(1, exitCode);
        Assert.Contains("FAIL", console.ToString());
        Assert.Contains("no fixture was written", console.ToString());
        Assert.False(File.Exists(fixture), "A failing builder must not produce a fixture.");
    }

    [Fact]
    public void Fixture_Should_ListTheCandidatesAndExitTwo_When_SubstringMatchesSeveralScenarios()
    {
        string fixture = Path.Combine(_outputRoot.FullName, "ambiguous.vcdbs");
        var console = new StringWriter();

        int exitCode = FixtureRunner.Run(
            new FixtureArguments(GuineaPigDll, "Scenario_Should", fixture), console, console);

        string text = console.ToString();
        Assert.Equal(2, exitCode);
        Assert.Contains("matches", text);
        Assert.Contains("exactly one", text);
        Assert.Contains("Scenario_Should_TimeOut_When_GameThreadWedges", text);
        Assert.Contains("Scenario_Should_FailSetup_When_ClassDoesNotDeriveFromBase", text);
        Assert.False(File.Exists(fixture));
    }

    [Fact]
    public void Fixture_Should_ExitOneWithoutAFixture_When_TheMatchPassesWithoutBootingAHost()
    {
        // Flush whatever host an earlier test's nested run may have left in the registry, so
        // the harvest below observes exactly the no-host state the plain fact produces.
        FixtureHarvest.ShutDownAndHarvestSavePath(out _);
        string fixture = Path.Combine(_outputRoot.FullName, "not-a-builder.vcdbs");
        var console = new StringWriter();

        // A plain xunit [Fact] passes without booting anything: there is no world to harvest,
        // and the command must say so instead of writing an empty fixture.
        int exitCode = FixtureRunner.Run(
            new FixtureArguments(BuilderDll, "PlainFact", fixture), console, console);

        Assert.Equal(1, exitCode);
        Assert.Contains("left no world save to harvest", console.ToString());
        Assert.False(File.Exists(fixture));
    }

    [Fact]
    public void Fixture_Should_ExitTwo_When_SubstringMatchesNothing()
    {
        var console = new StringWriter();

        int exitCode = FixtureRunner.Run(
            new FixtureArguments(GuineaPigDll, "NoSuchBuilderAnywhere", "out.vcdbs"), console, console);

        Assert.Equal(2, exitCode);
        Assert.Contains("no scenario matches", console.ToString());
    }

    [Fact]
    public void Fixture_Should_RefuseToOverwriteAndExitTwo_When_OutExistsWithoutForce()
    {
        string fixture = Path.Combine(_outputRoot.FullName, "precious.vcdbs");
        File.WriteAllText(fixture, "an existing fixture someone cares about");
        var console = new StringWriter();

        // The overwrite check runs before the builder: the command must refuse without booting.
        int exitCode = FixtureRunner.Run(
            new FixtureArguments(BuilderDll, BuilderScenario, fixture), console, console);

        Assert.Equal(2, exitCode);
        Assert.Contains("refusing to overwrite", console.ToString());
        Assert.Contains("--force", console.ToString());
        Assert.Equal("an existing fixture someone cares about", File.ReadAllText(fixture));
    }
}
