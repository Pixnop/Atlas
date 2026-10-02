using System.Diagnostics;
using Atlas.Engine.Tests.Support;

namespace Atlas.Engine.Tests;

/// <summary>Covers two builds of one mod identity (issue #170), through the atlas CLI over
/// Atlas.TwoBuilds.Scenarios: two classes, each staging a different build of BindingFixtureMod
/// from a folder of its own, with no ProjectReference to either. In one process the first build
/// to load is the only one the process can bind, so the engine refuses the second and boots green
/// without the mod; the boot now fails with an error saying so. With <c>--parallel</c> every
/// class runs in a process of its own, so each build runs as staged: that is the recipe the wiki
/// gives, and this test keeps it true.</summary>
[Trait("Category", "E2E")]
public class TwoBuildsTests
{
    [Fact]
    public void SequentialRun_Should_FailTheSecondBuildNamingTheRefusal_When_BothBuildsShareOneProcess()
    {
        CliResult result = RunCli("run", TestPaths.TwoBuildsDll);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Total: 2, Passed: 1, Failed: 1, Skipped: 0", result.StdOut, StringComparison.Ordinal);

        // The class that lost says why, naming both builds' files: not a missing mod, not a
        // green boot with a scenario failing on whatever the mod's absence causes.
        Assert.Contains("the engine refused to load it", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("an assembly named 'BindingFixtureMod' is already loaded", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Mod 'bindingfixture' was staged from '", result.StdOut, StringComparison.Ordinal);

        // The class that won is verified, and its line names the class and the file it bound.
        Assert.Matches(
            @"\[Atlas\] staged mod 'bindingfixture' for Atlas\.TwoBuilds\.Scenarios\.(Alpha|Beta)BuildScenarios: verified \(MVID [0-9a-f-]{36}, loaded from '[^']+BindingFixtureMod\.dll'\)",
            result.StdErr + result.StdOut);
    }

    [Fact]
    public void ParallelRun_Should_RunEachBuildAsStaged_When_EveryClassHasItsOwnWorker()
    {
        CliResult result = RunCli("run", TestPaths.TwoBuildsDll, "--parallel", "2");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Total: 2, Passed: 2, Failed: 0, Skipped: 0", result.StdOut, StringComparison.Ordinal);
    }

    private static CliResult RunCli(params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = TestPaths.OwnOutputDirectory,
        };
        startInfo.ArgumentList.Add(Path.Combine(TestPaths.OwnOutputDirectory, "Atlas.Cli.dll"));
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;

        // Both reads start before the wait, or a full pipe would hold the child back until the
        // deadline (see ParallelModeTests).
        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(240_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.True(exited, "The CLI process did not exit within its deadline.");
        return new CliResult(process.ExitCode, stdOutTask.GetAwaiter().GetResult(), stdErrTask.GetAwaiter().GetResult());
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
