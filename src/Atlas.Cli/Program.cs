namespace Atlas.Cli;

/// <summary>Entry point of the `atlas` dotnet tool. Deliberately a thin shell: it parses the
/// command line, validates preconditions, and dispatches to <see cref="ScenarioLister"/>,
/// <see cref="ScenarioRunner"/>, the multi-process orchestrator (<see cref="ParallelRunner"/>),
/// the worker-mode counterparts (<see cref="WorkerLister"/>, <see cref="WorkerRunner"/>),
/// the fixture builder (<see cref="FixtureRunner"/>), or the TRX comparison
/// (<see cref="DiffRunner"/>); every decision worth testing lives in the classes next to it.</summary>
internal static class Program
{
    /// <summary>Usage text printed for `--help` (and pointed at by usage errors).</summary>
    internal const string HelpText = """
        Atlas CLI: run Atlas scenarios from a compiled test assembly, without VSTest.

        Usage:
          atlas run <path/to/Scenarios.dll> [--filter <substring>] [--list]
                    [--worker [--classes <Fully.Qualified.ClassA,Fully.Qualified.ClassB>]]
                    [--parallel [N] [--worker-timeout <seconds>] [--trx <path>]]
          atlas fixture <path/to/Scenarios.dll> --scenario <substring> --out <fixture.vcdbs>
                    [--force]
          atlas diff <baseline.trx> <candidate.trx> [--json]

        Commands:
          run      Build nothing, boot the embedded server(s) in-process, and execute the
                   assembly's [AtlasScenario] methods sequentially, like `dotnet test` would.
          fixture  Run exactly ONE matching scenario as a world builder, then copy the world
                   save its graceful shutdown persisted to --out. The builder is an ordinary
                   [AtlasScenario] whose side effect is building the world (place blocks, run
                   commands, seed data); a class then boots the produced file with
                   [AtlasWorld(SaveFile = "fixtures/myworld.vcdbs")]. If the builder fails,
                   no fixture is written.
          diff     Compare two TRX reports by test name (baseline vs candidate; no server, no
                   VINTAGE_STORY) and report what changed: new failures, fixed, vanished, new
                   tests, still failing, and notable duration shifts (a passing test at least
                   2x and at least 500 ms away from its baseline). Works on the TRX atlas
                   writes (`--parallel --trx`) and on any spec-conforming TRX (`dotnet test
                   --logger trx`). Exit 0 when the candidate has no regressions, 1 when it
                   has (a regression is a new failure or a vanished test), 2 when a file
                   cannot be read as TRX. Contract: docs/specs/2026-07-14-diff-command.md.

        Options (run):
          --filter <substring>   Only scenarios whose display name contains the substring
                                 (ordinal, case-insensitive).
          --list                 Print the discovered scenarios and exit without booting
                                 anything (VINTAGE_STORY not required).
          --worker               Report exclusively as line-delimited JSON events on stdout
                                 (the orchestrator protocol; see
                                 docs/specs/2026-07-06-worker-protocol.md). With --list,
                                 emits one 'discovered' event per scenario instead.
          --classes <list>       Worker mode only: run only these scenario classes
                                 (comma-separated fully qualified names, exact match).
          --parallel [N]         Run the scenario classes on N worker subprocesses (one live
                                 server per worker, one class per dispatch). N defaults to
                                 min(cores / 2, class count). Incompatible with --worker
                                 and --list.
          --worker-timeout <s>   Parallel mode only: kill a worker stuck on one class for
                                 more than <s> seconds and report the class as failed
                                 (default 600).
          --trx <path>           Parallel mode only: write one aggregated VSTest-style TRX
                                 report covering every class.

        Options (fixture):
          --scenario <substring> Required: display-name substring selecting the builder
                                 scenario (ordinal, case-insensitive). It must match exactly
                                 one scenario; zero or several matches is a usage error.
          --out <path>           Required: where to write the .vcdbs fixture. Parent
                                 directories are created as needed; an existing file is only
                                 overwritten with --force.
          --force                Overwrite an existing --out file.

        Options (diff):
          --json                 Emit a versioned machine-readable JSON document (v 1) on
                                 stdout instead of the console listing.

        Options:
          -h, --help             Show this help.
          --version              Print the atlas version and exit (no assembly or
                                 VINTAGE_STORY required). `atlas version` works too.

        Environment:
          VINTAGE_STORY          Required by `run` and `fixture`: the Vintage Story install
                                 directory containing VintagestoryLib.dll.

        Exit codes:
          0  every scenario passed (or the listing succeeded, or the fixture was written, or
             the diff found no regressions)
          1  at least one scenario failed or errored, or nothing matched the filter, or the
             fixture builder failed (no fixture is written then), or the diff found
             regressions (a new failure or a vanished test)
          2  usage or environment error (bad arguments, VINTAGE_STORY missing, a diff input
             that cannot be read as TRX)
        """;

    private static int Main(string[] args)
    {
        CliParseResult parsed = CliArgumentsParser.Parse(args);
        if (parsed.ShowHelp)
        {
            Console.Out.WriteLine(HelpText);
            return 0;
        }

        if (parsed.ShowVersion)
        {
            Console.Out.WriteLine(CliVersion.Resolve());
            return 0;
        }

        if (parsed.Error is not null)
        {
            Console.Error.WriteLine($"atlas: {parsed.Error}");
            Console.Error.WriteLine("Run 'atlas --help' for usage.");
            return 2;
        }

        if (parsed.Diff is { } diffArguments)
        {
            // Pure file comparison: no scenario assembly and no VINTAGE_STORY involved.
            return DiffRunner.Run(diffArguments, Console.Out, Console.Error);
        }

        string assemblyPath = parsed.Fixture?.AssemblyPath ?? parsed.Arguments!.AssemblyPath;
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"atlas: scenario assembly not found: '{assemblyPath}'");
            return 2;
        }

        if (parsed.Fixture is { } fixtureArguments)
        {
            string? fixtureEnvironmentError = VintageStoryEnvironment.Validate(
                Environment.GetEnvironmentVariable(VintageStoryEnvironment.VariableName),
                File.Exists);
            if (fixtureEnvironmentError is not null)
            {
                Console.Error.WriteLine($"atlas: {fixtureEnvironmentError}");
                return 2;
            }

            return FixtureRunner.Run(fixtureArguments, Console.Out, Console.Error);
        }

        CliArguments arguments = parsed.Arguments!;
        var filter = new ScenarioFilter(arguments.Filter);
        if (arguments.Worker)
        {
            // Worker stdout carries EXCLUSIVELY protocol events: the embedded server logs to
            // the process console, so the console is rerouted to stderr (kept for forensics)
            // and only the event writer holds the real stdout.
            TextWriter eventStream = Console.Out;
            Console.SetOut(Console.Error);
            if (arguments.List)
            {
                return WorkerLister.List(arguments.AssemblyPath, filter, eventStream);
            }

            string? workerEnvironmentError = VintageStoryEnvironment.Validate(
                Environment.GetEnvironmentVariable(VintageStoryEnvironment.VariableName),
                File.Exists);
            if (workerEnvironmentError is not null)
            {
                // The runner reports the error on the event stream too, so the orchestrator
                // learns the reason without scraping stderr.
                Console.Error.WriteLine($"atlas: {workerEnvironmentError}");
            }

            return WorkerRunner.Run(arguments.AssemblyPath, filter, arguments.Classes, workerEnvironmentError, eventStream);
        }

        if (arguments.List)
        {
            return ScenarioLister.List(arguments.AssemblyPath, filter, Console.Out);
        }

        string? environmentError = VintageStoryEnvironment.Validate(
            Environment.GetEnvironmentVariable(VintageStoryEnvironment.VariableName),
            File.Exists);
        if (environmentError is not null)
        {
            Console.Error.WriteLine($"atlas: {environmentError}");
            return 2;
        }

        if (arguments.Parallel)
        {
            WorkerCommand command = WorkerCommand.Resolve(Environment.ProcessPath, typeof(Program).Assembly.Location);
            return ParallelRunner.Run(arguments, filter, command, Console.Out);
        }

        return ScenarioRunner.Run(arguments.AssemblyPath, filter, Console.Out);
    }
}
