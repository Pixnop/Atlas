using System.Xml;
using System.Xml.Linq;

namespace Atlas.Cli;

/// <summary>Implements `atlas diff`: loads the two TRX reports, compares them and prints what
/// changed. No server, no assembly, no VINTAGE_STORY: the command reads two files and decides.
/// Deliberately a thin IO shell: reading (<see cref="TrxResultsReader"/>), comparison
/// (<see cref="TrxDiff"/>) and both output shapes (<see cref="DiffConsoleReport"/>,
/// <see cref="DiffJsonReport"/>) are pure and tested on their own.</summary>
internal static class DiffRunner
{
    /// <summary>Runs the comparison.</summary>
    /// <param name="arguments">The parsed command line.</param>
    /// <param name="output">Destination for the report (console listing or JSON document).</param>
    /// <param name="error">Destination for IO and format errors.</param>
    /// <returns>The process exit code: 0 no regressions, 1 regressions (a new failure or a
    /// vanished test), 2 when either file cannot be read as TRX.</returns>
    public static int Run(DiffArguments arguments, TextWriter output, TextWriter error)
    {
        if (!TryRead(arguments.BaselinePath, "baseline", error, out IReadOnlyList<TrxTestResult> baseline)
            || !TryRead(arguments.CandidatePath, "candidate", error, out IReadOnlyList<TrxTestResult> candidate))
        {
            return 2;
        }

        DiffResult diff = TrxDiff.Compute(baseline, candidate);
        if (arguments.Json)
        {
            output.WriteLine(DiffJsonReport.Serialize(diff, arguments.BaselinePath, arguments.CandidatePath));
        }
        else
        {
            foreach (string line in DiffConsoleReport.Lines(diff, arguments.BaselinePath, arguments.CandidatePath))
            {
                output.WriteLine(line);
            }
        }

        return diff.ExitCode;
    }

    private static bool TryRead(string path, string role, TextWriter error, out IReadOnlyList<TrxTestResult> results)
    {
        results = [];
        try
        {
            results = TrxResultsReader.Read(XDocument.Load(path));
            return true;
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or XmlException or FormatException)
        {
            error.WriteLine($"atlas: cannot read the {role} TRX '{path}': {failure.Message}");
            return false;
        }
    }
}
