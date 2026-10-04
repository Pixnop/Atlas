using System.Diagnostics;
using System.Reflection;
using Atlas.Engine.Tests.Support;
using Atlas.Internal.Bootstrap;
using Atlas.XUnit.Internal;
using Vintagestory.API.Config;

namespace Atlas.Engine.Tests;

/// <summary>Covers the game version line every boot writes to stderr, and the refusal an assembly
/// can ask for with <c>[assembly: AtlasRequireCompiledGameVersion]</c>: the host-level behavior on
/// a real boot, the version the build stamped into this very assembly, and the line coming out of
/// both CLI paths (<c>atlas run</c> and its <c>--worker</c> half) as a real subprocess.</summary>
/// <remarks>The line is written once per distinct text in a process, so each boot test compiles
/// against a version string of its own: an earlier boot of the suite cannot have used it up.</remarks>
[Trait("Category", "E2E")]
public class GameVersionLineTests
{
    // One scenario of the guinea pig that boots a server and passes: enough to make the CLI boot.
    private const string OneBootingScenario = "A_Scenario_Should_Pass_When_RollbackWorldIsRequested";

    private static string Install => Environment.GetEnvironmentVariable("VINTAGE_STORY")!;

    private static string RequiredVersionDll { get; } =
        typeof(GameVersionLineTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RequiredVersionDll").Value!;

    [Fact]
    public async Task StartAsync_Should_WriteTheGameVersionLine_When_AHostBoots()
    {
        var compiled = new CompiledGameVersion("0.0.1-line", Required: false);
        await using ServerHost host = NewHost(compiled);

        string stderr = await CaptureStderrAsync(() => host.StartAsync());

        Assert.Contains(
            $"[Atlas] game {EngineCompat.ShortGameVersion} from '{Install}' (scenarios compiled against 0.0.1-line)",
            stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_Should_BootOnAnotherInstall_When_TheVersionIsNotRequired()
    {
        // The cross-install run (issue #49): a build made against one game, run on another.
        var compiled = new CompiledGameVersion("0.0.1-other-game", Required: false);
        await using ServerHost host = NewHost(compiled);

        Exception? thrown = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.Null(thrown);
    }

    [Fact]
    public async Task StartAsync_Should_RefuseBeforeTheServerStarts_When_TheCompiledVersionIsRequiredAndDiffers()
    {
        var compiled = new CompiledGameVersion("0.0.1-required", Required: true);
        await using ServerHost host = NewHost(compiled);

        AtlasSetupException ex = null!;
        string stderr = await CaptureStderrAsync(async () =>
            ex = await Assert.ThrowsAsync<AtlasSetupException>(() => host.StartAsync()));

        Assert.Contains("0.0.1-required", ex.Message, StringComparison.Ordinal);
        Assert.Contains(EngineCompat.ShortGameVersion, ex.Message, StringComparison.Ordinal);
        Assert.Contains(Install, ex.Message, StringComparison.Ordinal);
        Assert.Contains("AtlasRequireCompiledGameVersion", ex.Message, StringComparison.Ordinal);

        // Before the server starts: no game thread was ever spawned, and the line still says which
        // game the refused boot would have run on.
        Assert.Null(host.GameThread);
        Assert.Contains("(scenarios compiled against 0.0.1-required)", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_Should_Boot_When_TheCompiledVersionIsRequiredAndIsTheInstalls()
    {
        var compiled = new CompiledGameVersion(EngineCompat.ShortGameVersion, Required: true);
        await using ServerHost host = NewHost(compiled);

        Exception? thrown = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.Null(thrown);
    }

    [Fact]
    public void Map_Should_CarryTheVersionTheBuildStamped_When_TheTestAssemblyWasBuilt()
    {
        // build/Atlas.E2E.targets stamps the const the compiler read from the referenced API. The
        // same const, read here, is baked into this assembly by the same compiler in the same
        // build, so the two agree whatever install the suite later runs against.
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(GameVersionLineTests));

        Assert.Equal(GameVersion.ShortGameVersion, recipe.CompiledGameVersion.Version);
        Assert.False(recipe.CompiledGameVersion.Required);
    }

    [Fact]
    public void AtlasRun_Should_WriteTheGameVersionLineToStderr_When_AScenarioBoots()
    {
        CliResult result = RunCli("run", TestPaths.GuineaPigDll, "--filter", OneBootingScenario);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(ExpectedLine(), result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void AtlasRunWorker_Should_WriteTheGameVersionLineToStderrAndNothingToStdout_When_AScenarioBoots()
    {
        CliResult result = RunCli("run", TestPaths.GuineaPigDll, "--worker", "--filter", OneBootingScenario);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(ExpectedLine(), result.StdErr, StringComparison.Ordinal);

        // Stdout carries protocol events and nothing else: the line is stderr only.
        Assert.DoesNotContain("[Atlas] game", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public void AtlasRun_Should_FailTheScenarioBeforeAnyServerStarts_When_TheAssemblyRequiresAVersionTheInstallIsNot()
    {
        // Atlas.RequiredVersion.Scenarios declares [assembly: AtlasRequireCompiledGameVersion] and
        // carries a compiled version no install has. Through the CLI the refusal is the scenario's
        // own failure, naming both versions and the install; the TMPDIR of its own shows that no
        // host ever got as far as a scratch directory.
        DirectoryInfo temp = Directory.CreateTempSubdirectory("atlas-requiredversion-");
        try
        {
            CliResult result = RunCli(["run", RequiredVersionDll], Path.GetFullPath(temp.FullName));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("AtlasSetupException", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("compiled against game 0.0.1-stale", result.StdOut, StringComparison.Ordinal);
            Assert.Contains($"which is game {GameVersion.ShortGameVersion}", result.StdOut, StringComparison.Ordinal);
            Assert.Contains(Install, result.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                $"[Atlas] game {GameVersion.ShortGameVersion} from '{Install}' (scenarios compiled against 0.0.1-stale)",
                result.StdErr,
                StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(temp.FullName, "atlas")), "a refused boot left a scratch directory");
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    // The GuineaPig assembly is built in the same build, against the same install, as this one.
    private static string ExpectedLine()
        => $"[Atlas] game {GameVersion.ShortGameVersion} from '{Install}' " +
            $"(scenarios compiled against {GameVersion.ShortGameVersion})";

    private static ServerHost NewHost(CompiledGameVersion? compiled)
        => new(new WorldOptions(), [], TestPaths.OwnOutputDirectory) { CompiledAgainst = compiled };

    private static async Task<string> CaptureStderrAsync(Func<Task> action)
    {
        TextWriter original = Console.Error;

        // The game thread writes some lines too, so the writer handed to the console is the
        // synchronized wrapper; the text is read from the StringWriter behind it.
        var captured = new StringWriter();
        Console.SetError(TextWriter.Synchronized(captured));
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            Console.SetError(original);
        }

        lock (captured)
        {
            return captured.ToString();
        }
    }

    private static CliResult RunCli(params string[] args) => RunCli(args, tempDirectory: null);

    private static CliResult RunCli(string[] args, string? tempDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = TestPaths.OwnOutputDirectory,
        };
        if (tempDirectory is not null)
        {
            startInfo.Environment["TMPDIR"] = tempDirectory;
        }

        startInfo.ArgumentList.Add(Path.Combine(TestPaths.OwnOutputDirectory, "Atlas.Cli.dll"));
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;

        // Started before the wait, like WorkerModeTests.RunWorker: a synchronous read would block
        // until the process exits and make the deadline below unreachable.
        Task<string> stdOut = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErr = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(120_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.True(exited, "The CLI process did not exit within its deadline.");
        return new CliResult(process.ExitCode, stdOut.GetAwaiter().GetResult(), stdErr.GetAwaiter().GetResult());
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
