using Atlas.Internal;

namespace Atlas.Pure.Tests.Diagnostics;

public class PluralTests
{
    [Theory]
    [InlineData(0, "0 ticks")]
    [InlineData(1, "1 tick")]
    [InlineData(2, "2 ticks")]
    [InlineData(1200, "1200 ticks")]
    public void Of_Should_AgreeTheNounWithTheCount_When_TheNounIsRegular(int count, string expected)
    {
        Assert.Equal(expected, Plural.Of(count, "tick"));
    }

    [Theory]
    [InlineData(1, "1 entry")]
    [InlineData(3, "3 entries")]
    public void Of_Should_UseTheGivenPlural_When_TheNounIsIrregular(int count, string expected)
    {
        Assert.Equal(expected, Plural.Of(count, "entry", "entries"));
    }
}
