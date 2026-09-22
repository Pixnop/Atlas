namespace Atlas.Pure.Tests.XUnit;

using Atlas.XUnit.Internal;

public class ScenarioSettingsTests
{
    [Fact]
    public void None_Should_HaveEveryFlagOff_When_ReadByAScenarioThatAskedForNothing()
    {
        ScenarioSettings none = ScenarioSettings.None;

        Assert.False(none.FreshWorld);
        Assert.False(none.RollbackWorld);
        Assert.False(none.RestartWorld);
        Assert.False(none.StrictIsolation);
        Assert.Equal(0, none.TimeoutMs);
    }
}
