using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>Serializes a <see cref="DiffResult"/> as the machine-readable document of
/// `atlas diff --json`. The shape is versioned like the worker protocol: every document carries
/// `v` (currently <see cref="Version"/>) first, `v` is bumped only when an existing field
/// changes meaning or disappears, new fields may be added without bumping it, and consumers must
/// ignore fields they do not know. Every category key is always present (empty arrays stay
/// empty, they never disappear). `tests`, the opt-in per-test listing behind `--json-tests`, is
/// the exception: it is omitted entirely (not an empty array) unless requested, so the default
/// payload is unchanged. Documented in docs/specs/2026-07-14-diff-command.md.</summary>
internal static class DiffJsonReport
{
    /// <summary>Document version stamped on every emitted report.</summary>
    internal const int Version = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Serializes the whole report.</summary>
    /// <param name="diff">The computed diff.</param>
    /// <param name="baselinePath">The baseline TRX path, as the user gave it.</param>
    /// <param name="candidatePath">The candidate TRX path, as the user gave it.</param>
    /// <param name="tests">The per-test listing behind `--json-tests`; null when the flag was not
    /// given, which omits the `tests` key entirely so the default payload is unchanged.</param>
    /// <returns>The indented JSON document.</returns>
    public static string Serialize(
        DiffResult diff, string baselinePath, string candidatePath, IReadOnlyList<DiffTestEntry>? tests = null) =>
        JsonSerializer.Serialize(
            new Document(
                Version,
                new Run(baselinePath, diff.BaselineTotal),
                new Run(candidatePath, diff.CandidateTotal),
                new Counts(
                    diff.NewFailures.Count,
                    diff.FixedTests.Count,
                    diff.VanishedTests.Count,
                    diff.NewTests.Count,
                    diff.StillFailing.Count,
                    diff.DurationShifts.Count),
                diff.HasRegressions,
                diff.ExitCode,
                [.. diff.NewFailures.Select(f => new NewFailure(f.TestName, StateName(f.Baseline), f.Message))],
                [.. diff.FixedTests.Select(name => new Test(name))],
                [.. diff.VanishedTests.Select(t => new Vanished(t.TestName, KindName(t.BaselineKind)))],
                [.. diff.NewTests.Select(t => new NewTest(t.TestName, KindName(t.CandidateKind)))],
                [.. diff.StillFailing.Select(name => new Test(name))],
                [.. diff.DurationShifts.Select(s => new Shift(
                    s.TestName, s.BaselineMs, s.CandidateMs, s.Slower ? "slower" : "faster"))],
                tests?.Select(t => new TestEntry(t.TestName, SideOf(t.Baseline), SideOf(t.Candidate), t.Candidate?.StdOut)).ToList()),
            Options);

    /// <summary>Projects one side of a merged test entry, or null when the test is absent from
    /// that side.</summary>
    private static Side? SideOf(TrxTestResult? result) =>
        result is null ? null : new Side(KindName(result.Kind), result.DurationMs);

    private static string StateName(DiffBaselineState state) => state switch
    {
        DiffBaselineState.Passed => "passed",
        DiffBaselineState.Skipped => "skipped",
        _ => "absent",
    };

    private static string KindName(TestOutcomeKind kind) => kind switch
    {
        TestOutcomeKind.Passed => "passed",
        TestOutcomeKind.Failed => "failed",
        _ => "skipped",
    };

    private sealed record Document(
        [property: JsonPropertyName("v"), JsonPropertyOrder(-1)] int V,
        [property: JsonPropertyName("baseline")] Run Baseline,
        [property: JsonPropertyName("candidate")] Run Candidate,
        [property: JsonPropertyName("counts")] Counts Counts,
        [property: JsonPropertyName("regressions")] bool Regressions,
        [property: JsonPropertyName("exitCode")] int ExitCode,
        [property: JsonPropertyName("newFailures")] IReadOnlyList<NewFailure> NewFailures,
        [property: JsonPropertyName("fixed")] IReadOnlyList<Test> Fixed,
        [property: JsonPropertyName("vanished")] IReadOnlyList<Vanished> Vanished,
        [property: JsonPropertyName("new")] IReadOnlyList<NewTest> New,
        [property: JsonPropertyName("stillFailing")] IReadOnlyList<Test> StillFailing,
        [property: JsonPropertyName("durationShifts")] IReadOnlyList<Shift> DurationShifts,
        [property: JsonPropertyName("tests"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<TestEntry>? Tests = null);

    private sealed record Run(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("tests")] int Tests);

    private sealed record Counts(
        [property: JsonPropertyName("newFailures")] int NewFailures,
        [property: JsonPropertyName("fixed")] int Fixed,
        [property: JsonPropertyName("vanished")] int Vanished,
        [property: JsonPropertyName("new")] int New,
        [property: JsonPropertyName("stillFailing")] int StillFailing,
        [property: JsonPropertyName("durationShifts")] int DurationShifts);

    private sealed record NewFailure(
        [property: JsonPropertyName("test")] string TestName,
        [property: JsonPropertyName("baseline")] string Baseline,
        [property: JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message);

    private sealed record Test([property: JsonPropertyName("test")] string TestName);

    private sealed record Vanished(
        [property: JsonPropertyName("test")] string TestName,
        [property: JsonPropertyName("baseline")] string Baseline);

    private sealed record NewTest(
        [property: JsonPropertyName("test")] string TestName,
        [property: JsonPropertyName("outcome")] string Outcome);

    private sealed record Shift(
        [property: JsonPropertyName("test")] string TestName,
        [property: JsonPropertyName("baselineMs")] long BaselineMs,
        [property: JsonPropertyName("candidateMs")] long CandidateMs,
        [property: JsonPropertyName("direction")] string Direction);

    /// <summary>One `--json-tests` entry: a merged test identity paired across both runs, with
    /// the candidate's stdout when it carries one.</summary>
    private sealed record TestEntry(
        [property: JsonPropertyName("test")] string TestName,
        [property: JsonPropertyName("baseline")] Side? Baseline,
        [property: JsonPropertyName("candidate")] Side? Candidate,
        [property: JsonPropertyName("stdout"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StdOut);

    /// <summary>One side (baseline or candidate) of a `--json-tests` entry.</summary>
    private sealed record Side(
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("durationMs")] long? DurationMs);
}
