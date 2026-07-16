using System.Text.Json;
using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class DiffJsonReportTests
{
    [Fact]
    public void Serialize_Should_StampTheVersionFirst_When_Emitting()
    {
        using JsonDocument document = Parse(Mixed());

        Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
        Assert.Equal("v", document.RootElement.EnumerateObject().First().Name);
    }

    [Fact]
    public void Serialize_Should_DescribeBothRuns_When_Emitting()
    {
        using JsonDocument document = Parse(Mixed());

        JsonElement baseline = document.RootElement.GetProperty("baseline");
        JsonElement candidate = document.RootElement.GetProperty("candidate");
        Assert.Equal("base.trx", baseline.GetProperty("path").GetString());
        Assert.Equal(6, baseline.GetProperty("tests").GetInt32());
        Assert.Equal("cand.trx", candidate.GetProperty("path").GetString());
        Assert.Equal(7, candidate.GetProperty("tests").GetInt32());
    }

    [Fact]
    public void Serialize_Should_CountEveryCategory_When_Emitting()
    {
        using JsonDocument document = Parse(Mixed());

        JsonElement counts = document.RootElement.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("newFailures").GetInt32());
        Assert.Equal(1, counts.GetProperty("fixed").GetInt32());
        Assert.Equal(1, counts.GetProperty("vanished").GetInt32());
        Assert.Equal(1, counts.GetProperty("new").GetInt32());
        Assert.Equal(1, counts.GetProperty("stillFailing").GetInt32());
        Assert.Equal(1, counts.GetProperty("durationShifts").GetInt32());
    }

    [Fact]
    public void Serialize_Should_CarryTheVerdict_When_Emitting()
    {
        using JsonDocument regressed = Parse(Mixed());
        using JsonDocument clean = Parse(Empty());

        Assert.True(regressed.RootElement.GetProperty("regressions").GetBoolean());
        Assert.Equal(1, regressed.RootElement.GetProperty("exitCode").GetInt32());
        Assert.False(clean.RootElement.GetProperty("regressions").GetBoolean());
        Assert.Equal(0, clean.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void Serialize_Should_DetailEveryCategoryEntry_When_Emitting()
    {
        using JsonDocument document = Parse(Mixed());

        JsonElement failure = document.RootElement.GetProperty("newFailures")[0];
        Assert.Equal("Ns.A.Breaks", failure.GetProperty("test").GetString());
        Assert.Equal("passed", failure.GetProperty("baseline").GetString());
        Assert.Equal("Assert.Equal() Failure", failure.GetProperty("message").GetString());

        Assert.Equal("Ns.A.GetsFixed", document.RootElement.GetProperty("fixed")[0].GetProperty("test").GetString());

        JsonElement vanished = document.RootElement.GetProperty("vanished")[0];
        Assert.Equal("Ns.A.Vanishes", vanished.GetProperty("test").GetString());
        Assert.Equal("passed", vanished.GetProperty("baseline").GetString());

        JsonElement fresh = document.RootElement.GetProperty("new")[0];
        Assert.Equal("Ns.A.Appears", fresh.GetProperty("test").GetString());
        Assert.Equal("passed", fresh.GetProperty("outcome").GetString());

        Assert.Equal(
            "Ns.A.KeepsFailing", document.RootElement.GetProperty("stillFailing")[0].GetProperty("test").GetString());

        JsonElement shift = document.RootElement.GetProperty("durationShifts")[0];
        Assert.Equal("Ns.A.SlowsDown", shift.GetProperty("test").GetString());
        Assert.Equal(200, shift.GetProperty("baselineMs").GetInt64());
        Assert.Equal(1400, shift.GetProperty("candidateMs").GetInt64());
        Assert.Equal("slower", shift.GetProperty("direction").GetString());
    }

    [Fact]
    public void Serialize_Should_KeepEveryCategoryKey_When_TheCategoriesAreEmpty()
    {
        // The stable-shape guarantee: consumers can index the arrays without existence checks.
        using JsonDocument document = Parse(Empty());

        foreach (string key in (string[])["newFailures", "fixed", "vanished", "new", "stillFailing", "durationShifts"])
        {
            Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty(key).ValueKind);
            Assert.Equal(0, document.RootElement.GetProperty(key).GetArrayLength());
        }
    }

    [Fact]
    public void Serialize_Should_OmitTheMessageKey_When_TheFailureCarriesNone()
    {
        var diff = new DiffResult(
            1, 1, [new DiffFailure("Ns.A.T", DiffBaselineState.Absent, null)], [], [], [], [], []);

        using JsonDocument document = JsonDocument.Parse(DiffJsonReport.Serialize(diff, "b", "c"));

        JsonElement failure = document.RootElement.GetProperty("newFailures")[0];
        Assert.Equal("absent", failure.GetProperty("baseline").GetString());
        Assert.False(failure.TryGetProperty("message", out _));
    }

    [Fact]
    public void Serialize_Should_ProduceTheSameDocument_When_SerializedTwice()
    {
        Assert.Equal(
            DiffJsonReport.Serialize(Mixed(), "base.trx", "cand.trx"),
            DiffJsonReport.Serialize(Mixed(), "base.trx", "cand.trx"));
    }

    [Fact]
    public void Serialize_Should_OmitTheTestsKey_When_NoTestsAreGiven()
    {
        // The default --json payload is unchanged: no tests argument means no `tests` key at
        // all, not an empty array.
        using JsonDocument document = Parse(Mixed());

        Assert.False(document.RootElement.TryGetProperty("tests", out _));
    }

    [Fact]
    public void Serialize_Should_DescribeBothSides_When_ATestExistsInBothRuns()
    {
        var tests = new[]
        {
            new DiffTestEntry(
                "Ns.A.T",
                new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, 10),
                new TrxTestResult("Ns.A.T", TestOutcomeKind.Failed, 12)),
        };

        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", tests));

        JsonElement entry = document.RootElement.GetProperty("tests")[0];
        Assert.Equal("Ns.A.T", entry.GetProperty("test").GetString());
        Assert.Equal("passed", entry.GetProperty("baseline").GetProperty("outcome").GetString());
        Assert.Equal(10, entry.GetProperty("baseline").GetProperty("durationMs").GetInt64());
        Assert.Equal("failed", entry.GetProperty("candidate").GetProperty("outcome").GetString());
        Assert.Equal(12, entry.GetProperty("candidate").GetProperty("durationMs").GetInt64());
    }

    [Fact]
    public void Serialize_Should_NullTheBaseline_When_TheTestIsAbsentFromTheBaseline()
    {
        var tests = new[]
        {
            new DiffTestEntry("Ns.A.T", null, new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, 5)),
        };

        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", tests));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("tests")[0].GetProperty("baseline").ValueKind);
    }

    [Fact]
    public void Serialize_Should_NullTheCandidate_When_TheTestIsAbsentFromTheCandidate()
    {
        var tests = new[]
        {
            new DiffTestEntry("Ns.A.T", new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, 5), null),
        };

        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", tests));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("tests")[0].GetProperty("candidate").ValueKind);
    }

    [Fact]
    public void Serialize_Should_NullTheDuration_When_ItIsUnparseable()
    {
        var tests = new[]
        {
            new DiffTestEntry(
                "Ns.A.T",
                new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, DurationMs: null),
                null),
        };

        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", tests));

        Assert.Equal(
            JsonValueKind.Null,
            document.RootElement.GetProperty("tests")[0].GetProperty("baseline").GetProperty("durationMs").ValueKind);
    }

    [Fact]
    public void Serialize_Should_IncludeTheCandidatesStdOut_When_ItCarriesOne()
    {
        var tests = new[]
        {
            new DiffTestEntry(
                "Ns.A.T",
                new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, 5, StdOut: "baseline noise"),
                new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, 5, StdOut: "candidate noise")),
        };

        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", tests));

        // Only the candidate's stdout is exposed, never the baseline's.
        Assert.Equal("candidate noise", document.RootElement.GetProperty("tests")[0].GetProperty("stdout").GetString());
    }

    [Fact]
    public void Serialize_Should_OmitTheStdOutKey_When_TheCandidateCarriesNone()
    {
        var tests = new[]
        {
            new DiffTestEntry("Ns.A.T", null, new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, 5)),
        };

        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", tests));

        Assert.False(document.RootElement.GetProperty("tests")[0].TryGetProperty("stdout", out _));
    }

    [Fact]
    public void Serialize_Should_OmitTheStdOutKey_When_TheTestIsAbsentFromTheCandidate()
    {
        var tests = new[]
        {
            new DiffTestEntry(
                "Ns.A.T", new TrxTestResult("Ns.A.T", TestOutcomeKind.Passed, 5, StdOut: "baseline noise"), null),
        };

        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", tests));

        Assert.False(document.RootElement.GetProperty("tests")[0].TryGetProperty("stdout", out _));
    }

    [Fact]
    public void Serialize_Should_ProduceAnEmptyTestsArray_When_TheListingIsRequestedButNothingMerged()
    {
        using JsonDocument document = JsonDocument.Parse(
            DiffJsonReport.Serialize(Empty(), "base.trx", "cand.trx", []));

        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("tests").ValueKind);
        Assert.Equal(0, document.RootElement.GetProperty("tests").GetArrayLength());
    }

    private static JsonDocument Parse(DiffResult diff) =>
        JsonDocument.Parse(DiffJsonReport.Serialize(diff, "base.trx", "cand.trx"));

    private static DiffResult Mixed() => new(
        6,
        7,
        [new DiffFailure("Ns.A.Breaks", DiffBaselineState.Passed, "Assert.Equal() Failure")],
        ["Ns.A.GetsFixed"],
        [new DiffVanishedTest("Ns.A.Vanishes", TestOutcomeKind.Passed)],
        [new DiffNewTest("Ns.A.Appears", TestOutcomeKind.Passed)],
        ["Ns.A.KeepsFailing"],
        [new DurationShift("Ns.A.SlowsDown", 200, 1400, Slower: true)]);

    private static DiffResult Empty() => new(3, 3, [], [], [], [], [], []);
}
