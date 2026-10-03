using System.Diagnostics;
using System.Runtime.Versioning;

namespace Atlas.Engine.Tests.RealClient;

/// <summary>The proof that a sandbox does not outlive its host: the test assembly is re-run as a
/// child process (see <see cref="SandboxHolder"/>) that starts a sandbox and waits, the test lists
/// everything in the sandbox, SIGKILLs the child after checking it is the child, and waits for
/// the whole list to be gone.</summary>
[UnsupportedOSPlatform("windows")]
internal static class HostKillProof
{
    /// <summary>Runs the proof.</summary>
    /// <param name="run">The folders of the test.</param>
    /// <param name="program">A stand-in program, or <see langword="null"/> for the real
    /// client.</param>
    /// <param name="mustSurviveNoLongerThan">How long after the kill the sandbox may still be
    /// there: the guardian's grace plus a margin.</param>
    /// <param name="waitUntilSettled">Runs once the sandbox's launcher is known and its client
    /// and display are up, to wait for whatever state the test wants the client in before the
    /// kill; it receives the run folder.</param>
    /// <param name="workingDirectory">The working folder of the holder, hence of the launcher it
    /// starts, or <see langword="null"/> for the test host's own.</param>
    /// <returns>How long the sandbox took to be gone after its host was killed; the task fails
    /// when it is not gone in time.</returns>
    public static async Task<TimeSpan> RunAsync(
        RealClientEnvironment.TestRun run,
        string? program,
        TimeSpan mustSurviveNoLongerThan,
        Func<string, Task> waitUntilSettled,
        string? workingDirectory = null)
    {
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
        var psi = new ProcessStartInfo(dotnet) { UseShellExecute = false, RedirectStandardOutput = true };
        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in new[]
        {
            Path.Combine(TestPaths.OwnOutputDirectory, "Atlas.Engine.Tests.dll"),
            SandboxHolder.Argument,
            run.RunDirectory,
            run.DataPath,
            program ?? "-",
            "300",
        })
        {
            psi.ArgumentList.Add(argument);
        }

        using Process holder = Process.Start(psi)!;
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            string? line = await holder.StandardOutput.ReadLineAsync(startup.Token);
            Assert.True(line is not null && line.StartsWith("LAUNCHER ", StringComparison.Ordinal), $"The holder said: {line}");
            int launcher = int.Parse(line["LAUNCHER ".Length..], System.Globalization.CultureInfo.InvariantCulture);

            await RealClientEnvironment.EventuallyAsync(
                () => ProcessTable.Descendants(launcher).Any(e => e.Comm == "Xvfb")
                      && ProcessTable.Descendants(launcher).Any(e => e.Comm == "timeout"),
                TimeSpan.FromSeconds(60),
                "the private display and the client's wrapper");
            await waitUntilSettled(run.RunDirectory);

            var everything = new List<ProcessTable.Entry>(ProcessTable.Descendants(launcher));
            everything.Insert(0, ProcessTable.Read(launcher)!.Value);
            Assert.Equal("unshare", everything[0].Comm);
            Assert.Equal("dotnet", ProcessTable.Read(holder.Id)!.Value.Comm);

            holder.Kill();
            await holder.WaitForExitAsync();
            var clock = Stopwatch.StartNew();

            string waitingFor = $"the sandbox to follow its killed host ({everything.Count} processes)";
            await RealClientEnvironment.EventuallyAsync(
                () => !everything.Any(ProcessTable.IsAlive), mustSurviveNoLongerThan, waitingFor);
            Assert.Contains("guardian: the host closed the pipe", File.ReadAllText(Path.Combine(run.RunDirectory, "sandbox.log")));
            return clock.Elapsed;
        }
        finally
        {
            if (!holder.HasExited)
            {
                holder.Kill();
            }
        }
    }
}
