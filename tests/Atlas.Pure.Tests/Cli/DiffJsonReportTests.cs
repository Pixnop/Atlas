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
