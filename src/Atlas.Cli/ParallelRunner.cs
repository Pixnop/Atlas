using System.Diagnostics;

namespace Atlas.Cli;

/// <summary>Implements `atlas run --parallel N`: discovers the assembly's scenario classes
/// in-process (no server boots for discovery), then drains a greedy per-class queue with N
/// worker subprocesses, each invoked as `atlas run &lt;dll&gt; --worker --classes &lt;one class&gt;`.
/// Results stream back over the worker JSONL protocol and are printed live; a worker that dies,
/// wedges past its outer timeout, or exits nonzero without explanation is translated into a
/// failed class without sinking the rest of the queue. Deliberately a thin process shell:
/// scheduling (<see cref="ClassWorkQueue"/>), invocation (<see cref="WorkerCommand"/>), stream
/// reading (<see cref="WorkerClassObservation"/>), crash translation
/// (<see cref="WorkerCrashTranslator"/>), aggregation (<see cref="ParallelRunReport"/>) and TRX
/// serialization (<see cref="TrxReport"/>) are all pure and tested on their own.</summary>
internal static class ParallelRunner
{
    /// <summary>Default outer per-worker timeout: generous defense in depth above the in-process
    /// per-scenario watchdog, for the day a worker wedges outside any scenario.</summary>
    internal const int DefaultWorkerTimeoutSeconds = 600;

    private static readonly object OutputLock = new();

    /// <summary>Runs the orchestrated parallel execution.</summary>
    /// <param name="arguments">The parsed command line (parallel mode).</param>
    /// <param name="filter">The display-name filter deciding which scenarios run.</param>
    /// <param name="command">How to re-invoke the atlas executable for workers.</param>
    /// <param name="output">Destination for live per-scenario lines and the summary.</param>
    /// <returns>The process exit code (0 all passed, 1 failures, crashes or empty run).</returns>
    public static int Run(CliArguments arguments, ScenarioFilter filter, WorkerCommand command, TextWriter output)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string assemblyPath = Path.GetFullPath(arguments.AssemblyPath);
        IReadOnlyList<DiscoveredScenario> scenarios = ScenarioDiscovery.Find(assemblyPath, filter);
        IReadOnlyList<string> classes = scenarios.Select(scenario => scenario.ClassName).Distinct().ToList();

        var report = new ParallelRunReport();
        var stopwatch = Stopwatch.StartNew();
        if (classes.Count > 0)
        {
            int workers = ParallelDegree.Resolve(arguments.ParallelDegree, Environment.ProcessorCount, classes.Count);
            int timeoutSeconds = arguments.WorkerTimeoutSeconds ?? DefaultWorkerTimeoutSeconds;
            WriteLine(output, $"Running {scenarios.Count} scenario(s) in {classes.Count} class(es) on {workers} worker(s).");

            var queue = new ClassWorkQueue(classes);
            Task[] loops = [.. Enumerable.Range(0, workers).Select(_ => Task.Run(
                () => WorkerLoop(queue, command, assemblyPath, arguments.Filter, timeoutSeconds, report, output)))];
            Task.WaitAll(loops);
        }

        foreach (string line in report.Summary(stopwatch.ElapsedMilliseconds))
        {
            WriteLine(output, line);
        }

        int exitCode = report.ExitCode;
        if (arguments.TrxPath is not null && !TryWriteTrx(arguments.TrxPath, assemblyPath, report, started, output))
        {
            exitCode = Math.Max(exitCode, 1);
        }

        return exitCode;
    }

    private static void WorkerLoop(
        ClassWorkQueue queue,
        WorkerCommand command,
        string assemblyPath,
        string? filter,
        int timeoutSeconds,
        ParallelRunReport report,
        TextWriter output)
    {
        while (queue.TryTake(out string? className))
        {
            RunClass(className!, command, assemblyPath, filter, timeoutSeconds, report, output);
        }
    }

    private static void RunClass(
        string className,
        WorkerCommand command,
        string assemblyPath,
        string? filter,
        int timeoutSeconds,
        ParallelRunReport report,
        TextWriter output)
    {
        var observation = new WorkerClassObservation();
        var stopwatch = Stopwatch.StartNew();
        WorkerExit exit = Execute(
            BuildStartInfo(command, assemblyPath, className, filter), observation, timeoutSeconds, report, output);
        stopwatch.Stop();

        if (WorkerCrashTranslator.Translate(className, observation, exit, timeoutSeconds) is { } translated)
        {
            WriteLine(output, report.RecordTest(translated));
        }

        foreach (WorkerClassSummary summary in observation.ClassSummaries)
        {
            WriteLine(output, report.RecordIsolationSummary(summary));
        }

        WriteLine(output, report.RecordClass(className, stopwatch.ElapsedMilliseconds));
    }

    private static ProcessStartInfo BuildStartInfo(
        WorkerCommand command, string assemblyPath, string className, string? filter)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in command.LeadingArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--classes");
        startInfo.ArgumentList.Add(className);
        if (filter is not null)
        {
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add(filter);
        }

        return startInfo;
    }

    private static WorkerExit Execute(
        ProcessStartInfo startInfo,
        WorkerClassObservation observation,
        int timeoutSeconds,
        ParallelRunReport report,
        TextWriter output)
    {
        try
        {
            using Process process = Process.Start(startInfo)!;
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Task pump = Task.Run(() => PumpEvents(process, observation, report, output));
            bool exited = process.WaitForExit((int)Math.Min(timeoutSeconds * 1000L, int.MaxValue));
            if (!exited)
            {
                KillQuietly(process);
            }

            WaitQuietly(pump);
            return new WorkerExit(exited ? process.ExitCode : null, TimedOut: !exited, StderrOf(stderr));
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // The worker could not even be spawned; crash translation turns this into a failed
            // class carrying the reason, and the queue moves on.
            return new WorkerExit(ExitCode: null, TimedOut: false, failure.Message);
        }
    }

    private static void PumpEvents(
        Process process, WorkerClassObservation observation, ParallelRunReport report, TextWriter output)
    {
        while (process.StandardOutput.ReadLine() is { } line)
        {
            if (observation.AcceptLine(line) is { } outcome)
            {
                WriteLine(output, report.RecordTest(outcome));
            }
        }
    }

    private static bool TryWriteTrx(
        string trxPath, string assemblyPath, ParallelRunReport report, DateTimeOffset started, TextWriter output)
    {
        try
        {
            var info = new TrxRunInfo(
                $"atlas run --parallel {Path.GetFileName(assemblyPath)}",
                assemblyPath,
                Environment.MachineName,
                started,
                DateTimeOffset.UtcNow);
            string fullPath = Path.GetFullPath(trxPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            TrxReport.Build(info, report.Outcomes, report.IsolationSummaryLines).Save(fullPath);
            WriteLine(output, $"TRX report written to {fullPath}");
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            WriteLine(output, $"atlas: failed to write the TRX report to '{trxPath}': {failure.Message}");
            return false;
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            // The whole tree: `dotnet <dll>` workers put the actual runner under the muxer.
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // The process exited in the race window between the timeout and the kill.
        }
    }

    private static void WaitQuietly(Task task)
    {
        try
        {
            task.Wait(TimeSpan.FromSeconds(15));
        }
        catch (AggregateException)
        {
            // A broken stdout pipe on a killed worker is not worth failing the class twice for:
            // crash translation already reports the death.
        }
    }

    private static string StderrOf(Task<string> stderr)
    {
        try
        {
            return stderr.Wait(TimeSpan.FromSeconds(15)) ? stderr.Result : string.Empty;
        }
        catch (AggregateException)
        {
            return string.Empty;
        }
    }

    private static void WriteLine(TextWriter output, string line)
    {
        lock (OutputLock)
        {
            output.WriteLine(line);
        }
    }
}
