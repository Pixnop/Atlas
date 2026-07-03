namespace Atlas.Pure.Tests.Api;

public class WorldOptionsTests
{
    [Fact]
    public void Ctor_Should_UseDeterministicDefaults_When_NothingSpecified()
    {
        var options = new WorldOptions();
        Assert.Equal("424242", options.Seed);
        Assert.Equal("creativebuilding", options.PlayStyle);
        Assert.Equal("superflat", options.WorldType);
    }
}
