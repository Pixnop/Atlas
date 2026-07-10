namespace Atlas.Cli;

/// <summary>Outcome of parsing the command line: exactly one of <see cref="Arguments"/>,
/// <see cref="Fixture"/>, <see cref="Error"/> or <see cref="ShowHelp"/> is meaningful.</summary>
internal sealed class CliParseResult
{
    private CliParseResult(CliArguments? arguments, FixtureArguments? fixture, string? error, bool showHelp)
    {
        Arguments = arguments;
        Fixture = fixture;
        Error = error;
        ShowHelp = showHelp;
    }

    /// <summary>Gets the singleton result asking the shell to print usage and exit successfully.</summary>
    public static CliParseResult Help { get; } = new(null, null, null, true);

    /// <summary>Gets the parsed `run` arguments; null unless a `run` invocation parsed successfully.</summary>
    public CliArguments? Arguments { get; }

    /// <summary>Gets the parsed `fixture` arguments; null unless a `fixture` invocation parsed
    /// successfully.</summary>
    public FixtureArguments? Fixture { get; }

    /// <summary>Gets the usage error message; null unless parsing failed.</summary>
    public string? Error { get; }

    /// <summary>Gets a value indicating whether the user asked for help.</summary>
    public bool ShowHelp { get; }

    /// <summary>Creates a successful result carrying parsed `run` arguments.</summary>
    /// <param name="arguments">The parsed arguments.</param>
    /// <returns>The successful result.</returns>
    public static CliParseResult Success(CliArguments arguments) => new(arguments, null, null, false);

    /// <summary>Creates a successful result carrying parsed `fixture` arguments.</summary>
    /// <param name="fixture">The parsed arguments.</param>
    /// <returns>The successful result.</returns>
    public static CliParseResult ForFixture(FixtureArguments fixture) => new(null, fixture, null, false);

    /// <summary>Creates a failed result carrying a usage error message.</summary>
    /// <param name="error">The usage error, without trailing punctuation-heavy framing.</param>
    /// <returns>The failed result.</returns>
    public static CliParseResult Failure(string error) => new(null, null, error, false);
}
