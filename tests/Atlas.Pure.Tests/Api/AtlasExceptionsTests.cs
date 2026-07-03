namespace Atlas.Pure.Tests.Api;

using Atlas.Api;

public class AtlasExceptionsTests
{
    [Fact]
    public void ScenarioTimeoutException_Should_CarryTicksWaited_When_Constructed()
    {
        var ex = new ScenarioTimeoutException("predicate never became true", ticksWaited: 200);
        Assert.Equal(200, ex.TicksWaited);
        Assert.Contains("predicate", ex.Message);
    }
}
