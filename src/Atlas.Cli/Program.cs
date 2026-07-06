namespace Atlas.Cli;

/// <summary>Entry point of the `atlas` dotnet tool. Deliberately a thin shell: it parses the
/// command line, validates preconditions, and dispatches to <see cref="ScenarioLister"/> or
/// <see cref="ScenarioRunner"/>; every decision worth testing lives in the classes next to it.</summary>
internal static class Program
{
    /// <summary>Usage text printed for `--help` (and pointed at by usage errors).</summary>
    internal const string HelpText = """
        Atlas CLI: run Atlas scenarios from a compiled test assembly, without VSTest.

        Usage:
          atlas run <path/to/Scenarios.dll> [--filter <substring>] [--list]

        Commands:
          run    Build nothing, boot the embedded server(s) in-process, and execute the
                 assembly's [AtlasScenario] methods sequentially, like `dotnet test` would.

        Options:
          --filter <substring>   Only scenarios whose display name contains the substring
                                 (ordinal, case-insensitive).
          --list                 Print the discovered scenarios and exit without booting
                                 anything (VINTAGE_STORY not required).
          -h, --help             Show this help.

        Environment:
          VINTAGE_STORY          Required by `run`: the Vintage Story install directory
                                 containing VintagestoryLib.dll.

        Exit codes:
          0  every scenario passed (or the listing succeeded)
          1  at least one scenario failed or errored, or nothing matched the filter
          2  usage or environment error
        """;

    private static int Main(string[] args)
    {
        CliParseResult parsed = CliArgumentsParser.Parse(args);
        if (parsed.ShowHelp)
        {
            Console.Out.WriteLine(HelpText);
            return 0;
        }

        if (parsed.Error is not null)
        {
            Console.Error.WriteLine($"atlas: {parsed.Error}");
            Console.Error.WriteLine("Run 'atlas --help' for usage.");
            return 2;
        }

        CliArguments arguments = parsed.Arguments!;
        if (!File.Exists(arguments.AssemblyPath))
        {
            Console.Error.WriteLine($"atlas: scenario assembly not found: '{arguments.AssemblyPath}'");
            return 2;
        }

        var filter = new ScenarioFilter(arguments.Filter);
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

        return ScenarioRunner.Run(arguments.AssemblyPath, filter, Console.Out);
    }
}
