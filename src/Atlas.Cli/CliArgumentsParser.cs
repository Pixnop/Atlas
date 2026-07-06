namespace Atlas.Cli;

/// <summary>Hand-rolled parser for the atlas command line (one command, a handful of options:
/// not worth a parsing library). Pure and side-effect free.</summary>
internal static class CliArgumentsParser
{
    private const string FilterOption = "--filter";
    private const string ClassesOption = "--classes";

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
        bool worker = false;
        IReadOnlyList<string>? classes = null;

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
            else if (arg == "--worker")
            {
                worker = true;
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
            else if (arg == ClassesOption)
            {
                if (i + 1 >= args.Count)
                {
                    return CliParseResult.Failure($"{ClassesOption} requires a value");
                }

                if (ParseClasses(args[++i], out classes) is { } error)
                {
                    return CliParseResult.Failure(error);
                }
            }
            else if (arg.StartsWith(ClassesOption + "=", StringComparison.Ordinal))
            {
                if (ParseClasses(arg[(ClassesOption.Length + 1)..], out classes) is { } error)
                {
                    return CliParseResult.Failure(error);
                }
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

        if (classes is not null && !worker)
        {
            return CliParseResult.Failure($"{ClassesOption} requires --worker");
        }

        return assemblyPath is null
            ? CliParseResult.Failure("missing scenario assembly path (usage: atlas run path/to/Scenarios.dll)")
            : CliParseResult.Success(new CliArguments(assemblyPath, filter, list, worker, classes));
    }

    private static string? ParseClasses(string value, out IReadOnlyList<string>? classes)
    {
        classes = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (classes.Count == 0)
        {
            classes = null;
            return $"{ClassesOption} requires at least one fully qualified class name";
        }

        return null;
    }

    private static bool IsHelpToken(string arg) => arg is "--help" or "-h" or "help";
}
