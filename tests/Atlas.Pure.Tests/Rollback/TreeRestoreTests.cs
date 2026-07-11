using Atlas.Internal.Rollback;
using Vintagestory.API.Datastructures;

namespace Atlas.Pure.Tests.Rollback;

/// <summary>Covers the in-place attribute-tree reset the player-aware rollback relies on: values
/// return to the baseline, keys added after the baseline disappear, and, critically, a sub-tree
/// present on both sides keeps its OBJECT IDENTITY, because engine behaviors cache sub-tree
/// references at initialization (e.g. the hunger behavior) and would otherwise keep writing to
/// detached trees after a rollback.</summary>
public class TreeRestoreTests
{
    [Fact]
    public void ApplyInPlace_Should_ResetChangedValues_When_LiveDivergedFromBaseline()
    {
        var live = new TreeAttribute();
        live.SetFloat("stability", 0.2f);
        live.SetString("phase", "polluted");
        var baseline = new TreeAttribute();
        baseline.SetFloat("stability", 1.0f);
        baseline.SetString("phase", "captured");

        TreeRestore.ApplyInPlace(live, baseline);

        Assert.Equal(1.0f, live.GetFloat("stability"));
        Assert.Equal("captured", live.GetString("phase"));
    }

    [Fact]
    public void ApplyInPlace_Should_RemoveKeys_When_TheyWereAddedAfterTheBaseline()
    {
        var live = new TreeAttribute();
        live.SetBool("eventFlag", true);
        live.GetOrAddTreeAttribute("pollutionTree").SetInt("count", 3);
        var baseline = new TreeAttribute();

        TreeRestore.ApplyInPlace(live, baseline);

        Assert.Equal(0, live.Count);
    }

    [Fact]
    public void ApplyInPlace_Should_AddKeysBack_When_LiveLostThem()
    {
        var live = new TreeAttribute();
        var baseline = new TreeAttribute();
        baseline.SetInt("deaths", 0);
        baseline.GetOrAddTreeAttribute("hunger").SetFloat("currentsaturation", 1500f);

        TreeRestore.ApplyInPlace(live, baseline);

        Assert.Equal(0, live.GetInt("deaths"));
        Assert.Equal(1500f, live.GetTreeAttribute("hunger").GetFloat("currentsaturation"));
    }

    [Fact]
    public void ApplyInPlace_Should_PreserveSubTreeIdentity_When_SubTreeExistsOnBothSides()
    {
        // The cached-reference contract: a behavior that stored this exact tree object at
        // initialization must observe the reset through its cached reference.
        var live = new TreeAttribute();
        ITreeAttribute cachedByBehavior = live.GetOrAddTreeAttribute("hunger");
        cachedByBehavior.SetFloat("currentsaturation", 40f);
        var baseline = new TreeAttribute();
        baseline.GetOrAddTreeAttribute("hunger").SetFloat("currentsaturation", 1500f);

        TreeRestore.ApplyInPlace(live, baseline);

        Assert.Same(cachedByBehavior, live.GetTreeAttribute("hunger"));
        Assert.Equal(1500f, cachedByBehavior.GetFloat("currentsaturation"));
    }

    [Fact]
    public void ApplyInPlace_Should_ResetNestedTreesRecursively_When_TreesNestSeveralLevels()
    {
        var live = new TreeAttribute();
        ITreeAttribute liveInner = live.GetOrAddTreeAttribute("outer").GetOrAddTreeAttribute("inner");
        liveInner.SetInt("value", 9);
        liveInner.SetBool("extra", true);
        var baseline = new TreeAttribute();
        baseline.GetOrAddTreeAttribute("outer").GetOrAddTreeAttribute("inner").SetInt("value", 1);

        TreeRestore.ApplyInPlace(live, baseline);

        Assert.Same(liveInner, live.GetTreeAttribute("outer").GetTreeAttribute("inner"));
        Assert.Equal(1, liveInner.GetInt("value"));
        Assert.False(liveInner.HasAttribute("extra"));
    }

    [Fact]
    public void ApplyInPlace_Should_ReplaceLeafWithTree_When_BaselineHadATreeWhereLiveHasALeaf()
    {
        var live = new TreeAttribute();
        live.SetInt("shape", 5);
        var baseline = new TreeAttribute();
        baseline.GetOrAddTreeAttribute("shape").SetInt("depth", 2);

        TreeRestore.ApplyInPlace(live, baseline);

        Assert.Equal(2, live.GetTreeAttribute("shape").GetInt("depth"));
    }

    [Fact]
    public void ApplyInPlace_Should_Throw_When_EitherTreeIsNull()
    {
        var tree = new TreeAttribute();

        Assert.Throws<ArgumentNullException>(() => TreeRestore.ApplyInPlace(null!, tree));
        Assert.Throws<ArgumentNullException>(() => TreeRestore.ApplyInPlace(tree, null!));
    }
}
