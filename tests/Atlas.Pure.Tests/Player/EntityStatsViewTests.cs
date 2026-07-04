namespace Atlas.Pure.Tests.Player;

using Atlas.Internal.Player;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

public class EntityStatsViewTests
{
    [Fact]
    public void Health_Should_ReadCurrentHealthFromTree_When_HealthTreePresent()
    {
        var entity = new FakeEntity();
        ITreeAttribute healthTree = entity.WatchedAttributes.GetOrAddTreeAttribute("health");
        healthTree.SetFloat("currenthealth", 14.5f);
        healthTree.SetFloat("maxhealth", 20f);

        var stats = new EntityStatsView(entity);

        Assert.Equal(14.5f, stats.Health);
        Assert.Equal(20f, stats.MaxHealth);
    }

    [Fact]
    public void Health_Should_ReturnZero_When_NoHealthTree()
    {
        var entity = new FakeEntity();

        var stats = new EntityStatsView(entity);

        Assert.Equal(0f, stats.Health);
        Assert.Equal(0f, stats.MaxHealth);
    }

    [Fact]
    public void Saturation_Should_ReadCurrentSaturationFromTree_When_HungerTreePresent()
    {
        var entity = new FakeEntity();
        ITreeAttribute hungerTree = entity.WatchedAttributes.GetOrAddTreeAttribute("hunger");
        hungerTree.SetFloat("currentsaturation", 42f);

        var stats = new EntityStatsView(entity);

        Assert.Equal(42f, stats.Saturation);
    }

    [Fact]
    public void Saturation_Should_ReturnZero_When_NoHungerTree()
    {
        var entity = new FakeEntity();

        var stats = new EntityStatsView(entity);

        Assert.Equal(0f, stats.Saturation);
    }

    [Fact]
    public void Attribute_Should_ReadTopLevelValue_When_PathIsSingleKey()
    {
        var entity = new FakeEntity();
        entity.WatchedAttributes.SetFloat("tempStab", 0.75f);

        var stats = new EntityStatsView(entity);

        Assert.Equal(0.75f, stats.Attribute<float>("tempStab"));
    }

    [Fact]
    public void Attribute_Should_ReadNestedValue_When_PathHasSlash()
    {
        var entity = new FakeEntity();
        ITreeAttribute hungerTree = entity.WatchedAttributes.GetOrAddTreeAttribute("hunger");
        hungerTree.SetFloat("currentsaturation", 33f);

        var stats = new EntityStatsView(entity);

        Assert.Equal(33f, stats.Attribute<float>("hunger/currentsaturation"));
    }

    [Fact]
    public void Attribute_Should_ReturnNull_When_PathDoesNotResolve()
    {
        var entity = new FakeEntity();

        var stats = new EntityStatsView(entity);

        Assert.Null(stats.Attribute<float?>("doesnotexist"));
        Assert.Null(stats.Attribute<float?>("nope/child"));
    }

    [Fact]
    public void Attribute_Should_ConvertAcrossNumericTypes_When_StoredTypeDiffers()
    {
        var entity = new FakeEntity();
        entity.WatchedAttributes.SetInt("level", 7);

        var stats = new EntityStatsView(entity);

        Assert.Equal(7d, stats.Attribute<double>("level"));
        Assert.Equal("7", stats.Attribute<string>("level"));
    }

    /// <summary>Minimal concrete <see cref="Entity"/> for pure tests: <see cref="Entity"/> is
    /// abstract but declares no abstract members, so a trivial subclass is enough to exercise
    /// <see cref="Entity.WatchedAttributes"/> without booting a server.</summary>
    private sealed class FakeEntity : Entity
    {
    }
}
