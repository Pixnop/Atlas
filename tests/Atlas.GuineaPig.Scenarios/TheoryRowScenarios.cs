using Atlas.XUnit;
using Xunit;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>Exercises <c>[AtlasTheory]</c> end-to-end through the nested runner
/// (<c>TheoryNestedRunnerTests</c> in Atlas.Engine.Tests, which runs only this class):
/// serializable <c>[InlineData]</c> rows must run as one scenario each with the row's values in
/// the display name and fail independently (only the second row fails here); non-serializable
/// <c>[MemberData]</c> rows must still execute per-row through the runtime-enumeration fallback
/// test case; and a theory with no data attributes must surface xUnit's own
/// "No data found for ..." failure instead of silently passing.</summary>
[AtlasWorld(Seed = 930)]
public class TheoryRowScenarios : AtlasScenarioBase
{
    public static IEnumerable<object[]> NonSerializableRows =>
        new[]
        {
            new object[] { new TheoryRowMarker("alpha") },
            new object[] { new TheoryRowMarker("beta") }
        };

    [AtlasTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Theory_Should_FailOnlySecondRow_When_RowsRunIndependently(int row)
    {
        await World.Ticks(1);
        Assert.NotEqual(2, row); // row 2 fails deliberately; rows 1 and 3 must still pass
    }

    [AtlasTheory]
    [MemberData(nameof(NonSerializableRows))]
    public async Task Theory_Should_RunEachRow_When_DataRowsAreNotSerializable(TheoryRowMarker marker)
    {
        await World.Ticks(1);
        Assert.False(string.IsNullOrEmpty(marker.Name));
    }

    // The missing data IS the tested behavior: this theory must surface xUnit's own
    // "No data found for ..." failure, which is exactly what xUnit1003 exists to prevent.
#pragma warning disable xUnit1003 // Theory methods must have test data
    [AtlasTheory]
    public Task Theory_Should_FailWithNoDataFound_When_TheoryHasNoDataAttributes(int value)
    {
        Assert.Fail($"unreachable: xUnit must fail this theory with 'No data found' before the body runs (value: {value})");
        return Task.CompletedTask;
    }
#pragma warning restore xUnit1003
}
