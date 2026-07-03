using Vintagestory.API.MathTools;

namespace Atlas.Pure.Tests.Api;

public class WorldAreaTests
{
    [Fact]
    public void Ctor_Should_StoreBoundsAndDimension_When_Constructed()
    {
        var bounds = new Cuboidi(1, 2, 3, 4, 5, 6);

        var area = new WorldArea(bounds, Dimension: 2);

        Assert.Equal(bounds, area.Bounds);
        Assert.Equal(2, area.Dimension);
    }

    [Fact]
    public void ImplicitCuboidiConversion_Should_ReturnBounds_When_Applied()
    {
        var bounds = new Cuboidi(1, 2, 3, 4, 5, 6);
        var area = new WorldArea(bounds, Dimension: 1);

        Cuboidi converted = area;

        Assert.Equal(bounds, converted);
        Assert.Same(bounds, converted);
    }

    [Fact]
    public void Area_Should_InheritSourceBlockPosDimension_When_Called()
    {
        var p = new BlockPos(10, 20, 30, 3);

        WorldArea area = p.Area(2);

        Assert.Equal(3, area.Dimension);
    }

    [Fact]
    public void Area_Should_ComputeSameBoundsAsBefore_When_Called()
    {
        var p = new BlockPos(10, 20, 30, 0);

        WorldArea area = p.Area(5);

        Assert.Equal(5, area.Bounds.X1);
        Assert.Equal(15, area.Bounds.Y1);
        Assert.Equal(25, area.Bounds.Z1);
        Assert.Equal(15, area.Bounds.X2);
        Assert.Equal(25, area.Bounds.Y2);
        Assert.Equal(35, area.Bounds.Z2);
    }

    [Fact]
    public void Corners_Should_BuildBlockPosWithAreaDimension_When_Called()
    {
        var bounds = new Cuboidi(1, 2, 3, 4, 5, 6);
        var area = new WorldArea(bounds, Dimension: 7);

        (BlockPos start, BlockPos end) = WorldArea.Corners(area);

        Assert.Equal(new BlockPos(1, 2, 3, 7), start);
        Assert.Equal(new BlockPos(4, 5, 6, 7), end);
    }
}
