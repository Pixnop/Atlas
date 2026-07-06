namespace Atlas.Cli;

/// <summary>Hand-rolled parser for the atlas command line (one command, two options: not worth a
/// parsing library). Pure and side-effect free.</summary>
internal static class CliArgumentsParser
{
    private const string FilterOption = "--filter";

    /// <summary>Parses the raw command line into a <see cref="CliParseResult"/>.</summary>
    /// <param name="args">The raw arguments, without the executable name.</param>
    /// <returns>Parsed arguments, a usage error, or a help request.</returns>
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelpToken(args[0]))
        {
            return CliParseResult.Help;
        }

        if (args[0] != "run")
        {
            return CliParseResult.Failure($"unknown command '{args[0]}' (expected 'run')");
        }

        string? assemblyPath = null;
        string? filter = null;
        bool list = false;

        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            if (IsHelpToken(arg))
            {
                return CliParseResult.Help;
            }

            if (arg == "--list")
            {
                list = true;
            }
            else if (arg == FilterOption)
            {
                if (i + 1 >= args.Count)
                {
                    return CliParseResult.Failure($"{FilterOption} requires a value");
                }

                filter = args[++i];
            }
            else if (arg.StartsWith(FilterOption + "=", StringComparison.Ordinal))
            {
                filter = arg[(FilterOption.Length + 1)..];
            }
            else if (arg.StartsWith('-'))
            {
                return CliParseResult.Failure($"unknown option '{arg}'");
            }
            else if (assemblyPath is not null)
            {
                return CliParseResult.Failure($"unexpected argument '{arg}' (assembly path already given: '{assemblyPath}')");
            }
            else
            {
                assemblyPath = arg;
            }
        }

        return assemblyPath is null
            ? CliParseResult.Failure("missing scenario assembly path (usage: atlas run path/to/Scenarios.dll)")
            : CliParseResult.Success(new CliArguments(assemblyPath, filter, list));
    }

    private static bool IsHelpToken(string arg) => arg is "--help" or "-h" or "help";
}
