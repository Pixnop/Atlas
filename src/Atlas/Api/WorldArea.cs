using Vintagestory.API.MathTools;

namespace Atlas.Api;

/// <summary>A cuboid area paired with the dimension it lives in.</summary>
/// <remarks>Vintage Story stores dimensions as stacked Y-offset slices of a single flat world:
/// a <see cref="BlockPos"/> carries a <c>dimension</c> field, and its <c>InternalY</c> folds
/// that dimension into one flat integer (<c>Y + dimension * BlockPos.DimensionBoundary</c>) so
/// engine box queries can normalize and filter correctly as long as both corners carry the same
/// dimension; <see cref="Cuboidi"/> itself has no dimension concept, which is why this type
/// exists to keep the two together.</remarks>
/// <param name="Bounds">The cuboid bounds, in that dimension's local block coordinates.</param>
/// <param name="Dimension">The dimension the bounds live in. 0 is the default overworld.</param>
public readonly record struct WorldArea(Cuboidi Bounds, int Dimension)
{
    /// <summary>Converts a <see cref="WorldArea"/> to its bounds, for call sites that only need
    /// the cuboid and do not care about dimension.</summary>
    /// <param name="area">The area to convert.</param>
    /// <returns><paramref name="area"/>'s <see cref="Bounds"/>.</returns>
    public static implicit operator Cuboidi(WorldArea area) => area.Bounds;

    /// <summary>Builds the two corner <see cref="BlockPos"/> instances for <paramref name="area"/>,
    /// each carrying <see cref="Dimension"/> so that dimension-aware engine queries (which
    /// normalize on <see cref="BlockPos.InternalY"/>) see the intended dimension.</summary>
    /// <param name="area">The area to build corners for.</param>
    /// <returns>The start corner (X1, Y1, Z1) and end corner (X2, Y2, Z2), both carrying
    /// <paramref name="area"/>'s dimension.</returns>
    public static (BlockPos Start, BlockPos End) Corners(WorldArea area)
    {
        Cuboidi bounds = area.Bounds;
        var start = new BlockPos(bounds.X1, bounds.Y1, bounds.Z1, area.Dimension);
        var end = new BlockPos(bounds.X2, bounds.Y2, bounds.Z2, area.Dimension);
        return (start, end);
    }
}
