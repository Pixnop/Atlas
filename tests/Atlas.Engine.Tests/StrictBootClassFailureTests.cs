using System.Diagnostics.CodeAnalysis;
using Atlas.Api;
using Atlas.Engine.Tests.Support;
using Atlas.XUnit;
using Atlas.XUnit.Internal;
using Xunit.Abstractions;

namespace Atlas.Engine.Tests;

/// <summary>Covers what a failed boot does to the rest of its class: the boot is tried once, its
/// failure is recorded, and every later scenario of the class fails at once with that failure
/// without booting a server again (before this, a class of eight scenarios under strict boot
/// diagnostics booted eight identical servers and kept eight scratch folders). Each test drives
/// real <c>AtlasTestCase</c>s of a private probe class through the full pipeline, the technique of
/// <see cref="ServerLogOnFailureTests"/>, with a TMPDIR of its own so the scratch folders it
/// counts are its own and no other Atlas process on the machine can disturb the count.</summary>
/// <remarks>Each probe class is used by one test only: a class whose boot failed stays failed for
/// the process, which is the behavior under test.</remarks>
[Trait("Category", "E2E")]
public sealed class StrictBootClassFailureTests : IDisposable
{
    private const string FixtureModPath = "../../../../BootDiagnosticsFixtureMod";
    private const string ServerLogLine = "[Atlas] server log: ";

    /// <summary>Why the probe bodies below carry no assertion of their own.</summary>
    private const string ProbeJustification =
        "Probe scenario driven by one of the tests above, which asserts what the pipeline reported " +
        "for it, not the body.";

    /// <summary>The folder the later-boot probe stages as its mod, relative to the test assembly's
    /// directory (where the registry resolves a class's mod paths), written and removed by the test.</summary>
    private const string LaterBootModFolder = "later-boot-probe-mod";

    private static readonly string[] TempVariables = ["TMPDIR", "TMP", "TEMP"];

    private static readonly string ProbeModFolder = Path.Combine(
        Path.GetDirectoryName(typeof(StrictBootClassFailureTests).Assembly.Location)!, LaterBootModFolder);

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-boot-once-");
    private readonly Dictionary<string, string?> _originalTemp = [];

    public StrictBootClassFailureTests()
    {
        // The scratch path is built from Path.GetTempPath() when a host is created, which follows
        // these variables at every call: pointing them at the test's own folder is what makes the
        // folders counted below exactly the ones this test's boots left.
        foreach (string variable in TempVariables)
        {
            _originalTemp[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, Path.GetFullPath(_root.FullName));
        }
    }

    private string ScratchRoot => Path.Combine(Path.GetFullPath(_root.FullName), "atlas");

    public void Dispose()
    {
        foreach ((string variable, string? original) in _originalTemp)
        {
            Environment.SetEnvironmentVariable(variable, original);
        }

        _root.Delete(recursive: true);
        DeleteProbeMod();
    }

    [Fact]
    public async Task StrictBootFailure_Should_FailTheClassOnce_When_SeveralScenariosFollowTheFailedBoot()
    {
        var failures = new List<ITestFailed>
        {
            await RunAsync(nameof(StrictClassScenarios.Scenario_Should_FailTheBoot), freshWorld: false, restartWorld: false),
            await RunAsync(nameof(StrictClassScenarios.Scenario_Should_FailAtOnce_AsFreshWorld), freshWorld: true, restartWorld: false),
            await RunAsync(nameof(StrictClassScenarios.Scenario_Should_FailAtOnce_AsRestartWorld), freshWorld: false, restartWorld: true),
            await RunAsync(nameof(StrictClassScenarios.Scenario_Should_FailAtOnce_AsRollbackWorld), freshWorld: false, restartWorld: false),
        };

        // One boot happened, so one scratch folder is kept: the first scenario's boot.
        string kept = Assert.Single(Directory.GetDirectories(ScratchRoot));
        string logPath = Path.Combine(kept, "Logs", "server-main.log");
        Assert.True(File.Exists(logPath), $"the kept folder has no '{logPath}'");

        // The first scenario sees the strict failure itself, naming the folder and the log.
        ITestFailed first = failures[0];
        Assert.Equal(typeof(AtlasBootDiagnosticsException).FullName, Assert.Single(first.ExceptionTypes));
        string firstMessage = Assert.Single(first.Messages);
        Assert.Contains("bootdiagfixture:blocktypes/malformed.json", firstMessage, StringComparison.Ordinal);
        Assert.Contains("entries at Warning level or above were logged", firstMessage, StringComparison.Ordinal);
        Assert.Contains(kept, firstMessage, StringComparison.Ordinal);
        Assert.Contains(logPath, firstMessage, StringComparison.Ordinal);

        // Every later scenario fails at once with the same failure, and says it is the class's
        // boot failure rather than a crash of its own.
        foreach (ITestFailed later in failures.Skip(1))
        {
            Assert.Equal(
                new[] { typeof(ServerCrashedException).FullName!, typeof(AtlasBootDiagnosticsException).FullName! },
                later.ExceptionTypes);
            string message = later.Messages[0];
            Assert.Contains("did not boot", message, StringComparison.Ordinal);
            Assert.Contains("bootdiagfixture:blocktypes/malformed.json", message, StringComparison.Ordinal);
            Assert.Contains("entries at Warning level or above were logged", message, StringComparison.Ordinal);
        }

        // Every one of them prints the failing scenario's server log block, pointing at the one
        // kept folder, with the engine's Error entries since that boot.
        foreach (ITestFailed failed in failures)
        {
            Assert.Contains(ServerLogLine + logPath, failed.Output, StringComparison.Ordinal);
            Assert.Contains("error(s) logged by the engine since the boot:", failed.Output, StringComparison.Ordinal);
            Assert.Contains("bootdiagfixture:blocktypes/malformed.json", failed.Output, StringComparison.Ordinal);
            Assert.Contains("bootdiagfixture:bootdiagbadproperty", failed.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OtherBootFailure_Should_FailTheClassOnceToo_When_TheModPathDoesNotExist()
    {
        ITestFailed first = await RunAsync(
            typeof(MissingModClassScenarios), nameof(MissingModClassScenarios.Scenario_Should_FailTheBoot), freshWorld: false, restartWorld: false);
        ITestFailed second = await RunAsync(
            typeof(MissingModClassScenarios), nameof(MissingModClassScenarios.Scenario_Should_FailAtOnce), freshWorld: true, restartWorld: false);

        Assert.Equal(typeof(AtlasSetupException).FullName, Assert.Single(first.ExceptionTypes));
        Assert.Contains("Mod path(s) not found", Assert.Single(first.Messages), StringComparison.Ordinal);

        Assert.Equal(
            new[] { typeof(ServerCrashedException).FullName!, typeof(AtlasSetupException).FullName! }, second.ExceptionTypes);
        string message = second.Messages[0];
        Assert.Contains("did not boot", message, StringComparison.Ordinal);
        Assert.Contains("Mod path(s) not found", message, StringComparison.Ordinal);

        // The boot never reached the engine: no log to point at, so no log line either.
        Assert.DoesNotContain(ServerLogLine, first.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(ServerLogLine, second.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaterBootFailure_Should_FailOnlyItsOwnScenario_When_TheClassBootedBefore()
    {
        try
        {
            // The class boots, then its mod folder disappears for one scenario: that boot fails, but
            // it is not the class's first, so the class is not marked and the next scenario boots.
            WriteProbeMod();
            IReadOnlyList<IMessageSinkMessage> first = await ProbeAsync(
                typeof(LaterBootClassScenarios), nameof(LaterBootClassScenarios.Scenario_Should_Boot), freshWorld: true, restartWorld: false);
            Assert.Single(first.OfType<ITestPassed>());

            DeleteProbeMod();
            ITestFailed second = await RunAsync(
                typeof(LaterBootClassScenarios), nameof(LaterBootClassScenarios.Scenario_Should_FailItsOwnBoot), freshWorld: true, restartWorld: false);
            Assert.Equal(typeof(AtlasSetupException).FullName, Assert.Single(second.ExceptionTypes));
            Assert.Contains("Mod path(s) not found", Assert.Single(second.Messages), StringComparison.Ordinal);

            WriteProbeMod();
            IReadOnlyList<IMessageSinkMessage> third = await ProbeAsync(
                typeof(LaterBootClassScenarios), nameof(LaterBootClassScenarios.Scenario_Should_BootAgain), freshWorld: true, restartWorld: false);
            Assert.Single(third.OfType<ITestPassed>());
        }
        finally
        {
            // The last scenario's host is live in the registry with its scratch folder inside this
            // test's TMPDIR root, which Dispose deletes: release it first, so the next test class
            // does not shut an engine down against a folder that is gone.
            HostRegistry.DisposeCurrentBestEffort();
        }
    }

    private static void WriteProbeMod()
    {
        Directory.CreateDirectory(ProbeModFolder);
        File.WriteAllText(
            Path.Combine(ProbeModFolder, "modinfo.json"),
            """{ "type": "content", "modid": "laterbootprobe", "name": "Later boot probe", "version": "0.1.0" }""");
    }

    private static void DeleteProbeMod()
    {
        if (Directory.Exists(ProbeModFolder))
        {
            Directory.Delete(ProbeModFolder, recursive: true);
        }
    }

    private static Task<ITestFailed> RunAsync(string method, bool freshWorld, bool restartWorld)
        => RunAsync(typeof(StrictClassScenarios), method, freshWorld, restartWorld);

    private static async Task<ITestFailed> RunAsync(Type probeClass, string method, bool freshWorld, bool restartWorld)
        => Assert.Single((await ProbeAsync(probeClass, method, freshWorld, restartWorld)).OfType<ITestFailed>());

    private static Task<IReadOnlyList<IMessageSinkMessage>> ProbeAsync(
        Type probeClass, string method, bool freshWorld, bool restartWorld)
        => ProbeCases.RunAsync(
            probeClass, method, strictIsolation: false, freshWorld: freshWorld, restartWorld: restartWorld);

#pragma warning disable xUnit1000 // Probe classes are driven through the pipeline by the tests above.

    /// <summary>Probe class whose boot fails strict: the fixture mod logs warnings and errors.</summary>
    [AtlasWorld(StrictBootDiagnostics = true, Mods = [FixtureModPath])]
    private sealed class StrictClassScenarios : AtlasScenarioBase
    {
        [AtlasScenario]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_FailTheBoot() => Task.CompletedTask;

        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_FailAtOnce_AsFreshWorld() => Task.CompletedTask;

        [AtlasScenario(RestartWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_FailAtOnce_AsRestartWorld() => Task.CompletedTask;

        [AtlasScenario]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_FailAtOnce_AsRollbackWorld() => Task.CompletedTask;
    }

    /// <summary>Probe class whose boot fails without strict mode: the mod path does not exist.</summary>
    [AtlasWorld(Mods = ["../../../../NoSuchModForTheBootOnceProbe"])]
    private sealed class MissingModClassScenarios : AtlasScenarioBase
    {
        [AtlasScenario]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_FailTheBoot() => Task.CompletedTask;

        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_FailAtOnce() => Task.CompletedTask;
    }

    /// <summary>Probe class staging a content mod folder the test writes and removes between its
    /// scenarios.</summary>
    [AtlasWorld(ExcludeAssemblyMods = true, Mods = [LaterBootModFolder])]
    private sealed class LaterBootClassScenarios : AtlasScenarioBase
    {
        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_Boot() => Task.CompletedTask;

        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_FailItsOwnBoot() => Task.CompletedTask;

        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_BootAgain() => Task.CompletedTask;
    }

#pragma warning restore xUnit1000
}
