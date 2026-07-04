using Vintagestory.API.MathTools;

namespace Atlas.Api;

/// <summary>Convenience helpers for building positions and areas around a <see cref="BlockPos"/>.</summary>
public static class BlockPosExtensions
{
    /// <summary>Returns a copy of <paramref name="p"/> offset by the given deltas.</summary>
    /// <param name="p">The base position.</param>
    /// <param name="dx">The X offset.</param>
    /// <param name="dy">The Y offset.</param>
    /// <param name="dz">The Z offset.</param>
    /// <returns>A new <see cref="BlockPos"/> offset from <paramref name="p"/>.</returns>
    public static BlockPos Offset(this BlockPos p, int dx, int dy, int dz) => p.AddCopy(dx, dy, dz);

    /// <summary>Builds a cuboid centered on <paramref name="p"/>, extending <paramref name="radius"/> blocks
    /// in every direction, inheriting <paramref name="p"/>'s dimension.</summary>
    /// <param name="p">The center position.</param>
    /// <param name="radius">The radius, in blocks, in every direction.</param>
    /// <returns>A <see cref="WorldArea"/> covering the requested area in <paramref name="p"/>'s dimension.</returns>
    public static WorldArea Area(this BlockPos p, int radius)
    {
        var cuboid = new Cuboidi();
        cuboid.Set(p.X - radius, p.Y - radius, p.Z - radius, p.X + radius, p.Y + radius, p.Z + radius);
        return new WorldArea(cuboid, p.dimension);
    }
}
