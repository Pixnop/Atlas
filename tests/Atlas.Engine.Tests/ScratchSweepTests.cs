using Atlas.Cli;
using Atlas.Internal.Hosting;
using Atlas.XUnit.Internal;
using Xunit.Runners;

namespace Atlas.Engine.Tests;

/// <summary>Proves the issue #83 scratch sweep end to end, through the same nested-runner
/// setup as <see cref="NestedRunnerTests"/> (which is also exactly how the atlas CLI executes
/// scenarios): a guinea pig class that ends green has its scratch directory deleted once the
/// registry hands its host off, a class with failing scenarios keeps its scratch (with
/// server-main.log inside: the documented post-mortem artifact), and ATLAS_KEEP_SCRATCH=1
/// keeps a green class's scratch too. The nested runs go through the full xUnit pipeline, so
/// they cover the failure recording in <c>AtlasTestRunner</c>, not only the registry's
/// decision.</summary>
/// <remarks>Every assertion targets the exact data path of the host under test, never a
/// listing of the shared temp root: other Atlas processes on the same machine (a parallel
/// worktree, `atlas run --parallel` workers) create and delete scratch directories there
/// concurrently, so set-difference assertions would be flaky. The nested run and this test
/// share <see cref="HostRegistry"/>'s statics (the runner loads the guinea pig assembly into
/// the default load context), which is what lets the test grab the class host, and its data
/// path, before triggering the hand-off. The probes stay green forever, so the hosts this
/// class leaves live are swept by whichever test requests the registry next (or at process
/// exit).</remarks>
[Trait("Category", "E2E")]
public class ScratchSweepTests
{
    [Fact]
    public async Task Scratch_Should_BeDeletedOnHandOff_When_ANestedClassEndsGreen()
    {
        // Only the passing rollback scenario of the isolation-activity guinea pig runs: one
        // real host boot, one green class.
        (int passed, int failed) = await RunGuineaPigClassAsync(
            "IsolationActivityScenarios",
            displayName => displayName.Contains("RollbackWorldIsRequested", StringComparison.Ordinal));
        Assert.Equal(1, passed);
        Assert.Equal(0, failed);

        // The green class still owns the live host: grab its scratch path, then hand the
        // registry to another class, which disposes and sweeps it.
        ServerHost classHost = await HostRegistry.GetOrCreateAsync(GuineaPigType("IsolationActivityScenarios"));
        string dataPath = classHost.DataPath;
        Assert.True(Directory.Exists(dataPath), $"the class host's scratch '{dataPath}' must exist while it is live");

        _ = await HostRegistry.GetOrCreateAsync(typeof(HandOffProbeScenarios));

        Assert.False(
            Directory.Exists(dataPath),
            $"the green class's scratch directory '{dataPath}' must be deleted at hand-off (issue #83)");
    }

    [Fact]
    public async Task Scratch_Should_SurviveWithTheServerLog_When_ANestedClassFails()
    {
        // The theory-row guinea pig fails rows with plain assertions on a healthy host, so the
        // ONLY thing keeping its scratch is the failure the pipeline recorded.
        (int passed, int failed) = await RunGuineaPigClassAsync("TheoryRowScenarios", displayNameFilter: null);
        Assert.True(failed > 0, "the guinea pig theory class must fail rows for this test to mean anything");
        Assert.True(passed > 0, "its passing rows must pass: the class host stays healthy");

        ServerHost classHost = await HostRegistry.GetOrCreateAsync(GuineaPigType("TheoryRowScenarios"));
        string dataPath = classHost.DataPath;

        _ = await HostRegistry.GetOrCreateAsync(typeof(HandOffProbeScenarios));

        try
        {
            Assert.True(
                Directory.Exists(dataPath),
                $"the failing class's scratch directory '{dataPath}' must survive the hand-off");
            string logMessage = "the kept scratch directory must contain the engine's server-main.log, " +
                "the post-mortem artifact the sweep exists to preserve";
            Assert.True(File.Exists(Path.Combine(dataPath, "Logs", "server-main.log")), logMessage);
        }
        finally
        {
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Scratch_Should_BeKeptOnHandOff_When_TheKeepVariableIsSet()
    {
        // Boot the probe host BEFORE setting the variable, so the hand-off it causes (of some
        // earlier test's live host) still sweeps normally and nothing unrelated is kept.
        ServerHost host = await HostRegistry.GetOrCreateAsync(typeof(KeepEnvProbeScenarios));
        string dataPath = host.DataPath;

        string? original = Environment.GetEnvironmentVariable(ScratchRetention.KeepScratchVariable);
        try
        {
            Environment.SetEnvironmentVariable(ScratchRetention.KeepScratchVariable, "1");
            _ = await HostRegistry.GetOrCreateAsync(typeof(HandOffProbeScenarios));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScratchRetention.KeepScratchVariable, original);
        }

        bool kept = Directory.Exists(dataPath);
        if (kept)
        {
            Directory.Delete(dataPath, recursive: true);
        }

        Assert.True(kept, "ATLAS_KEEP_SCRATCH=1 must keep even a green class's scratch directory");
    }

    /// <summary>Runs one guinea pig class through an in-process xunit runner (the CLI's own
    /// execution mechanism) and reports its pass/fail counts.</summary>
    /// <param name="className">Simple name of the guinea pig class to run.</param>
    /// <param name="displayNameFilter">Optional display-name filter selecting scenarios.</param>
    /// <returns>The number of passed and failed scenarios.</returns>
    private static async Task<(int Passed, int Failed)> RunGuineaPigClassAsync(
        string className, Func<string, bool>? displayNameFilter)
    {
        // Assembly.Location, not AppContext.BaseDirectory: the first host boot in the process
        // redirects BaseDirectory to the game install (see NestedRunnerTests).
        string dll = Path.Combine(
            Path.GetDirectoryName(typeof(ScratchSweepTests).Assembly.Location)!,
            "Atlas.GuineaPig.Scenarios.dll");
        Assert.True(File.Exists(dll), $"Guinea pig assembly not found at '{dll}'.");

        int passed = 0;
        int failed = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = AssemblyRunner.WithoutAppDomain(dll);
        try
        {
            if (displayNameFilter != null)
            {
                runner.TestCaseFilter = testCase => displayNameFilter(testCase.DisplayName);
            }

            runner.OnTestPassed = _ => Interlocked.Increment(ref passed);
            runner.OnTestFailed = _ => Interlocked.Increment(ref failed);
            runner.OnExecutionComplete = _ => done.TrySetResult();
            runner.Start(new AssemblyRunnerStartOptions
            {
                TypesToRun = [$"Atlas.GuineaPig.Scenarios.{className}"],
            });

            await done.Task.WaitAsync(TimeSpan.FromMinutes(2));
        }
        finally
        {
            // Bounded idle wait before Dispose; leaks the runner if it never idles (the xunit
            // 2.x AssemblyRunner disposal race, issue #59).
            RunnerDisposal.DisposeWhenIdle(runner);
        }

        return (passed, failed);
    }

    /// <summary>Resolves a guinea pig class by name from the assembly the nested runner loaded
    /// into the default context, so registry lookups against it hit the same statics.</summary>
    /// <param name="className">The class's simple name.</param>
    /// <returns>The resolved type.</returns>
    private static Type GuineaPigType(string className)
    {
        Type? type = Type.GetType($"Atlas.GuineaPig.Scenarios.{className}, Atlas.GuineaPig.Scenarios");
        Assert.True(type != null, $"could not resolve guinea pig class '{className}'");
        return type!;
    }

    /// <summary>Probe class the tests hand the registry to; never runs scenarios, never fails.</summary>
    private sealed class HandOffProbeScenarios
    {
    }

    /// <summary>Probe class whose green scratch the opt-out test expects to survive.</summary>
    private sealed class KeepEnvProbeScenarios
    {
    }
}
