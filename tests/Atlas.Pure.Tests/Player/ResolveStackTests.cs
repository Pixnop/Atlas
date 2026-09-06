using Atlas.Internal.Player;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Player;

/// <summary>The rules <c>ITestPlayer.GiveItem</c> applies before it touches an inventory,
/// checked against hand-built collectibles instead of a booted world: item before block, a
/// missing block is not a resolution, the quantity floor, and the cap of whichever collectible
/// the code resolved to. The messages and the reported parameter names are part of the public
/// contract, so they are asserted here rather than only read in an E2E failure.</summary>
public class ResolveStackTests
{
    private static readonly Item Flint = new() { Code = new AssetLocation("game:flint"), MaxStackSize = 64 };

    [Fact]
    public void ResolveStack_Should_ReturnTheItemStack_When_TheCodeIsAnItem()
    {
        ItemStack stack = TestPlayer.ResolveStack(_ => Flint, NoBlock, "game:flint", 3);

        Assert.Same(Flint, stack.Item);
        Assert.Equal(3, stack.StackSize);
    }

    [Fact]
    public void ResolveStack_Should_FallBackToTheBlock_When_TheCodeIsNotAnItem()
    {
        var soil = new Block { Code = new AssetLocation("game:soil-medium-normal"), MaxStackSize = 64 };

        ItemStack stack = TestPlayer.ResolveStack(NoItem, _ => soil, "game:soil-medium-normal", 2);

        Assert.Same(soil, stack.Block);
        Assert.Equal(2, stack.StackSize);
    }

    [Fact]
    public void ResolveStack_Should_ThrowArgumentException_When_TheBlockIsTheMissingPlaceholder()
    {
        // GetBlock hands back the "unknown block" placeholder rather than null for a code the
        // registry does not know, so IsMissing is what separates resolved from unresolved.
        var missing = new Block { Code = new AssetLocation("game:not-a-real-block"), IsMissing = true };

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => TestPlayer.ResolveStack(NoItem, _ => missing, "game:not-a-real-block", 1));

        Assert.Equal("itemOrBlockCode", ex.ParamName);
        Assert.Contains("Unknown item or block code 'game:not-a-real-block'", ex.Message);
    }

    [Fact]
    public void ResolveStack_Should_ThrowArgumentException_When_TheCodeIsNeitherItemNorBlock()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => TestPlayer.ResolveStack(NoItem, NoBlock, "game:not-a-real-item", 1));

        Assert.Equal("itemOrBlockCode", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ResolveStack_Should_ThrowArgumentOutOfRange_When_QuantityIsBelowOne(int quantity)
    {
        // Checked before any lookup: an invalid quantity is the caller's mistake whatever the
        // code turns out to name.
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => TestPlayer.ResolveStack(
                _ => throw new InvalidOperationException("the registries must not be read"),
                NoBlock,
                "game:flint",
                quantity));

        Assert.Equal("quantity", ex.ParamName);
    }

    [Fact]
    public void ResolveStack_Should_ThrowArgumentOutOfRange_When_QuantityExceedsTheResolvedMaxStackSize()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => TestPlayer.ResolveStack(_ => Flint, NoBlock, "game:flint", 65));

        Assert.Equal("quantity", ex.ParamName);
        Assert.Equal(65, ex.ActualValue);
        Assert.Contains("max stack size of 64", ex.Message);
    }

    [Fact]
    public void ResolveStack_Should_Accept_When_QuantityIsExactlyTheMaxStackSize()
    {
        ItemStack stack = TestPlayer.ResolveStack(_ => Flint, NoBlock, "game:flint", 64);

        Assert.Equal(64, stack.StackSize);
    }

    private static Item? NoItem(AssetLocation location) => null;

    private static Block? NoBlock(AssetLocation location) => null;
}
