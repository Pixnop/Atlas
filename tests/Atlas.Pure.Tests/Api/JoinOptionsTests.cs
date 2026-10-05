namespace Atlas.Pure.Tests.Api;

public class JoinOptionsTests
{
    [Fact]
    public void Ctor_Should_KeepTheEnginesRoleAndCollectItems_When_NothingSpecified()
    {
        var options = new JoinOptions();

        Assert.Null(options.Role);
        Assert.True(options.CollectItems);
    }

    [Fact]
    public void Equality_Should_BeValueBased_When_TheSameOptionsAreSet()
    {
        var options = new JoinOptions { Role = "suplayer", CollectItems = false };

        Assert.Equal(options, new JoinOptions { Role = "suplayer", CollectItems = false });
        Assert.NotEqual(options, options with { CollectItems = true });
        Assert.NotEqual(options, options with { Role = null });
    }
}
