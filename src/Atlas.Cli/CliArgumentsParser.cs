using System.Globalization;

namespace Atlas.Cli;

/// <summary>Hand-rolled parser for the atlas command line (two commands, a handful of options:
/// not worth a parsing library). Pure and side-effect free. Structured as a token cursor plus
/// one small handler per option, so each option's rules stay readable as the surface grows.</summary>
internal static class CliArgumentsParser
{
    private const string ListOption = "--list";
    private const string WorkerOption = "--worker";
    private const string FilterOption = "--filter";
    private const string ClassesOption = "--classes";
    private const string ParallelOption = "--parallel";
    private const string WorkerTimeoutOption = "--worker-timeout";
    private const string TrxOption = "--trx";
    private const string ScenarioOption = "--scenario";
    private const string OutOption = "--out";
    private const string ForceOption = "--force";
    private const string JsonOption = "--json";

    /// <summary>Parses the raw command line into a <see cref="CliParseResult"/>.</summary>
    /// <param name="args">The raw arguments, without the executable name.</param>
    /// <returns>Parsed arguments, a usage error, or a help request.</returns>
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || IsHelpToken(args[0]))
        {
            return CliParseResult.Help;
        }

        if (IsVersionToken(args[0]))
        {
            return CliParseResult.Version;
        }

        return args[0] switch
        {
            "run" => ParseCommand(args, new ParseState(), ApplyToken, Finish),
            "fixture" => ParseCommand(args, new FixtureParseState(), ApplyFixtureToken, FinishFixture),
            "diff" => ParseCommand(args, new DiffParseState(), ApplyDiffToken, FinishDiff),
            _ => CliParseResult.Failure($"unknown command '{args[0]}' (expected 'run', 'fixture' or 'diff')"),
        };
    }

    /// <summary>Shared option loop of every command: walks the tokens after the command name,
    /// honoring help and version tokens (the first one seen wins, like --help does) and stopping
    /// at the first usage error.</summary>
    /// <typeparam name="TState">The command's mutable parse state.</typeparam>
    /// <param name="args">The raw arguments, without the executable name.</param>
    /// <param name="state">The command's fresh parse state.</param>
    /// <param name="applyToken">The command's per-token handler; returns a usage error or null.</param>
    /// <param name="finish">The command's validation and packaging step.</param>
    /// <returns>Parsed arguments, a usage error, or a help request.</returns>
    private static CliParseResult ParseCommand<TState>(
        IReadOnlyList<string> args,
        TState state,
        Func<string, TokenCursor, TState, string?> applyToken,
        Func<TState, CliParseResult> finish)
    {
        var cursor = new TokenCursor(args, first: 1);
        while (cursor.TryTake(out string token))
        {
            if (IsHelpToken(token))
            {
                return CliParseResult.Help;
            }

            if (IsVersionToken(token))
            {
                return CliParseResult.Version;
            }

            if (applyToken(token, cursor, state) is { } error)
            {
                return CliParseResult.Failure(error);
            }
        }

        return finish(state);
    }

    private static string? ApplyToken(string token, TokenCursor cursor, ParseState state)
    {
        if (!token.StartsWith('-'))
        {
            return state.AcceptAssemblyPath(token);
        }

        (string name, string? inline) = SplitInlineValue(token);
        return name switch
        {
            ListOption when inline is null => state.EnableList(),
            WorkerOption when inline is null => state.EnableWorker(),
            FilterOption => state.SetFilter(inline ?? cursor.TakeOrNull()),
            ClassesOption => state.SetClasses(inline ?? cursor.TakeOrNull()),
            ParallelOption => state.EnableParallel(inline ?? cursor.TakeIntegerOrNull()),
            WorkerTimeoutOption => state.SetWorkerTimeout(inline ?? cursor.TakeOrNull()),
            TrxOption => state.SetTrxPath(inline ?? cursor.TakeOrNull()),
            _ => $"unknown option '{token}'",
        };
    }

    private static string? ApplyFixtureToken(string token, TokenCursor cursor, FixtureParseState state)
    {
        if (!token.StartsWith('-'))
        {
            return state.AcceptAssemblyPath(token);
        }

        (string name, string? inline) = SplitInlineValue(token);
        return name switch
        {
            ScenarioOption => state.SetScenario(inline ?? cursor.TakeOrNull()),
            OutOption => state.SetOutPath(inline ?? cursor.TakeOrNull()),
            ForceOption when inline is null => state.EnableForce(),
            _ => $"unknown option '{token}' for 'fixture'",
        };
    }

    private static string? ApplyDiffToken(string token, TokenCursor cursor, DiffParseState state)
    {
        if (!token.StartsWith('-'))
        {
            return state.AcceptPath(token);
        }

        (string name, string? inline) = SplitInlineValue(token);
        return name == JsonOption && inline is null
            ? state.EnableJson()
            : $"unknown option '{token}' for 'diff'";
    }

    private static CliParseResult FinishDiff(DiffParseState state)
    {
        if (state.BaselinePath is null)
        {
            return CliParseResult.Failure(
                "missing the two TRX paths (usage: atlas diff baseline.trx candidate.trx)");
        }

        return state.CandidatePath is null
            ? CliParseResult.Failure("missing the candidate TRX path (usage: atlas diff baseline.trx candidate.trx)")
            : CliParseResult.ForDiff(new DiffArguments(state.BaselinePath, state.CandidatePath, state.Json));
    }

    private static CliParseResult FinishFixture(FixtureParseState state)
    {
        if (state.AssemblyPath is null)
        {
            return CliParseResult.Failure(
                "missing scenario assembly path (usage: atlas fixture path/to/Scenarios.dll --scenario <substring> --out <fixture.vcdbs>)");
        }

        if (state.Scenario is null)
        {
            return CliParseResult.Failure($"{ScenarioOption} is required (the display-name substring selecting the builder scenario)");
        }

        return state.OutPath is null
            ? CliParseResult.Failure($"{OutOption} is required (where to write the .vcdbs fixture)")
            : CliParseResult.ForFixture(new FixtureArguments(state.AssemblyPath, state.Scenario, state.OutPath, state.Force));
    }

    private static CliParseResult Finish(ParseState state)
    {
        if (ValidateCombinations(state) is { } conflict)
        {
            return CliParseResult.Failure(conflict);
        }

        return state.AssemblyPath is null
            ? CliParseResult.Failure("missing scenario assembly path (usage: atlas run path/to/Scenarios.dll)")
            : CliParseResult.Success(new CliArguments(
                state.AssemblyPath,
                state.Filter,
                state.List,
                state.Worker,
                state.Classes,
                state.Parallel,
                state.ParallelDegree,
                state.WorkerTimeoutSeconds,
                state.TrxPath));
    }

    private static string? ValidateCombinations(ParseState state)
    {
        if (state.Classes is not null && !state.Worker)
        {
            return $"{ClassesOption} requires --worker";
        }

        if (state.Parallel && state.Worker)
        {
            return $"{ParallelOption} is incompatible with --worker (workers are what --parallel spawns)";
        }

        if (state.Parallel && state.List)
        {
            return $"{ParallelOption} is incompatible with --list (listing never spawns workers)";
        }

        if (state.WorkerTimeoutSeconds is not null && !state.Parallel)
        {
            return $"{WorkerTimeoutOption} requires {ParallelOption}";
        }

        if (state.TrxPath is not null && !state.Parallel)
        {
            return $"{TrxOption} requires {ParallelOption}";
        }

        return null;
    }

    private static (string Name, string? InlineValue) SplitInlineValue(string token)
    {
        int separator = token.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? (token, null) : (token[..separator], token[(separator + 1)..]);
    }

    private static bool IsHelpToken(string arg) => arg is "--help" or "-h" or "help";

    private static bool IsVersionToken(string arg) => arg is "--version" or "version";

    /// <summary>Forward-only cursor over the raw arguments, so option handlers can consume their
    /// value without anyone mutating a shared loop counter.</summary>
    private sealed class TokenCursor(IReadOnlyList<string> args, int first)
    {
        private int _next = first;

        /// <summary>Takes the next token, if any.</summary>
        /// <param name="token">The taken token; empty when the cursor is exhausted.</param>
        /// <returns>True when a token was taken.</returns>
        public bool TryTake(out string token)
        {
            if (_next >= args.Count)
            {
                token = string.Empty;
                return false;
            }

            token = args[_next];
            _next++;
            return true;
        }

        /// <summary>Takes the next token unconditionally, or returns null at the end.</summary>
        /// <returns>The taken token, or null.</returns>
        public string? TakeOrNull() => TryTake(out string token) ? token : null;

        /// <summary>Takes the next token only when it parses as an integer (the optional-value
        /// rule of `--parallel [N]`: a non-numeric next token is left for the positional slot).</summary>
        /// <returns>The taken integer-shaped token, or null.</returns>
        public string? TakeIntegerOrNull()
        {
            bool nextIsInteger = _next < args.Count
                && int.TryParse(args[_next], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            return nextIsInteger ? args[_next++] : null;
        }
    }

    /// <summary>Mutable accumulation of the `fixture` options seen so far; each setter returns a
    /// usage error or null, so <see cref="ApplyFixtureToken"/> stays a flat dispatch table.</summary>
    private sealed class FixtureParseState
    {
        /// <summary>Gets the positional assembly path, once seen.</summary>
        public string? AssemblyPath { get; private set; }

        /// <summary>Gets the value of --scenario, once seen.</summary>
        public string? Scenario { get; private set; }

        /// <summary>Gets the value of --out, once seen.</summary>
        public string? OutPath { get; private set; }

        /// <summary>Gets a value indicating whether --force was seen.</summary>
        public bool Force { get; private set; }

        /// <summary>Accepts the positional assembly path; at most one is allowed.</summary>
        /// <param name="token">The positional token.</param>
        /// <returns>A usage error, or null.</returns>
        public string? AcceptAssemblyPath(string token)
        {
            if (AssemblyPath is not null)
            {
                return $"unexpected argument '{token}' (assembly path already given: '{AssemblyPath}')";
            }

            AssemblyPath = token;
            return null;
        }

        /// <summary>Records the --scenario value.</summary>
        /// <param name="value">The raw value, or null when the command line ran out.</param>
        /// <returns>A usage error, or null.</returns>
        public string? SetScenario(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return $"{ScenarioOption} requires a value (a display-name substring)";
            }

            Scenario = value;
            return null;
        }

        /// <summary>Records the --out value.</summary>
        /// <param name="value">The raw fixture path, or null when the command line ran out.</param>
        /// <returns>A usage error, or null.</returns>
        public string? SetOutPath(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return $"{OutOption} requires a file path";
            }

            OutPath = value;
            return null;
        }

        /// <summary>Records the --force flag.</summary>
        /// <returns>Always null.</returns>
        public string? EnableForce()
        {
            Force = true;
            return null;
        }
    }

    /// <summary>Mutable accumulation of the `diff` tokens seen so far; each setter returns a
    /// usage error or null, so <see cref="ApplyDiffToken"/> stays a flat dispatch table.</summary>
    private sealed class DiffParseState
    {
        /// <summary>Gets the first positional path (the baseline TRX), once seen.</summary>
        public string? BaselinePath { get; private set; }

        /// <summary>Gets the second positional path (the candidate TRX), once seen.</summary>
        public string? CandidatePath { get; private set; }

        /// <summary>Gets a value indicating whether --json was seen.</summary>
        public bool Json { get; private set; }

        /// <summary>Accepts a positional TRX path: the first is the baseline, the second the
        /// candidate, a third is a usage error.</summary>
        /// <param name="token">The positional token.</param>
        /// <returns>A usage error, or null.</returns>
        public string? AcceptPath(string token)
        {
            if (BaselinePath is null)
            {
                BaselinePath = token;
                return null;
            }

            if (CandidatePath is null)
            {
                CandidatePath = token;
                return null;
            }

            return $"unexpected argument '{token}' (both TRX paths already given: '{BaselinePath}' and '{CandidatePath}')";
        }

        /// <summary>Records the --json flag.</summary>
        /// <returns>Always null.</returns>
        public string? EnableJson()
        {
            Json = true;
            return null;
        }
    }

    /// <summary>Mutable accumulation of the `run` options seen so far; each setter returns a usage
    /// error or null, so <see cref="ApplyToken"/> stays a flat dispatch table.</summary>
    private sealed class ParseState
    {
        /// <summary>Gets the positional assembly path, once seen.</summary>
        public string? AssemblyPath { get; private set; }

        /// <summary>Gets the value of --filter, once seen.</summary>
        public string? Filter { get; private set; }

        /// <summary>Gets a value indicating whether --list was seen.</summary>
        public bool List { get; private set; }

        /// <summary>Gets a value indicating whether --worker was seen.</summary>
        public bool Worker { get; private set; }

        /// <summary>Gets the parsed value of --classes, once seen.</summary>
        public IReadOnlyList<string>? Classes { get; private set; }

        /// <summary>Gets a value indicating whether --parallel was seen.</summary>
        public bool Parallel { get; private set; }

        /// <summary>Gets the explicit worker count of --parallel, when one was given.</summary>
        public int? ParallelDegree { get; private set; }

        /// <summary>Gets the value of --worker-timeout in seconds, once seen.</summary>
        public int? WorkerTimeoutSeconds { get; private set; }

        /// <summary>Gets the value of --trx, once seen.</summary>
        public string? TrxPath { get; private set; }

        /// <summary>Accepts the positional assembly path; at most one is allowed.</summary>
        /// <param name="token">The positional token.</param>
        /// <returns>A usage error, or null.</returns>
        public string? AcceptAssemblyPath(string token)
        {
            if (AssemblyPath is not null)
            {
                return $"unexpected argument '{token}' (assembly path already given: '{AssemblyPath}')";
            }

            AssemblyPath = token;
            return null;
        }

        /// <summary>Records the --list flag.</summary>
        /// <returns>Always null.</returns>
        public string? EnableList()
        {
            List = true;
            return null;
        }

        /// <summary>Records the --worker flag.</summary>
        /// <returns>Always null.</returns>
        public string? EnableWorker()
        {
            Worker = true;
            return null;
        }

        /// <summary>Records the --filter value.</summary>
        /// <param name="value">The raw value, or null when the command line ran out.</param>
        /// <returns>A usage error, or null.</returns>
        public string? SetFilter(string? value)
        {
            if (value is null)
            {
                return $"{FilterOption} requires a value";
            }

            Filter = value;
            return null;
        }

        /// <summary>Records and splits the --classes value.</summary>
        /// <param name="value">The raw comma-separated list, or null when the command line ran out.</param>
        /// <returns>A usage error, or null.</returns>
        public string? SetClasses(string? value)
        {
            if (value is null)
            {
                return $"{ClassesOption} requires a value";
            }

            string[] classes = value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (classes.Length == 0)
            {
                return $"{ClassesOption} requires at least one fully qualified class name";
            }

            Classes = classes;
            return null;
        }

        /// <summary>Records the --parallel flag and its optional worker count.</summary>
        /// <param name="value">The explicit count token, or null when none was given (the runner
        /// then computes the default).</param>
        /// <returns>A usage error, or null.</returns>
        public string? EnableParallel(string? value)
        {
            Parallel = true;
            if (value is null)
            {
                return null;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int degree) || degree < 1)
            {
                return $"{ParallelOption} requires a value >= 1";
            }

            ParallelDegree = degree;
            return null;
        }

        /// <summary>Records the --worker-timeout value.</summary>
        /// <param name="value">The raw value in seconds, or null when the command line ran out.</param>
        /// <returns>A usage error, or null.</returns>
        public string? SetWorkerTimeout(string? value)
        {
            if (value is null
                || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                || seconds < 1)
            {
                return $"{WorkerTimeoutOption} requires a whole number of seconds >= 1";
            }

            WorkerTimeoutSeconds = seconds;
            return null;
        }

        /// <summary>Records the --trx value.</summary>
        /// <param name="value">The raw report path, or null when the command line ran out.</param>
        /// <returns>A usage error, or null.</returns>
        public string? SetTrxPath(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return $"{TrxOption} requires a file path";
            }

            TrxPath = value;
            return null;
        }
    }
}
