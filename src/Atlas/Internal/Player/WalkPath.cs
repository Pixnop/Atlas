using Vintagestory.API.MathTools;

namespace Atlas.Internal.Player;

/// <summary>The geometry of <see cref="Api.ITestPlayer.WalkTo"/>: a straight line covered in
/// steps of a fixed length, one per game tick. It knows nothing of the engine, so how many steps
/// a distance takes and where each one lands is checkable without a booted server.</summary>
internal static class WalkPath
{
    /// <summary>The length of one step, in blocks. One step is taken per tick, which at the
    /// engine's 30 passes a second is 6 blocks a second: a little above a sprint, so a walk is
    /// quick, and short enough that the engine's sweep test misses no block a step passes.</summary>
    public const double StepBlocks = 0.2;

    /// <summary>Slack that keeps a distance that is a whole number of steps in decimal (0.8, say,
    /// which is 4.000000000000001 steps in binary) from costing one step more.</summary>
    private const double Slack = 1e-9;

    /// <summary>Counts the steps a walk of <paramref name="distance"/> blocks takes.</summary>
    /// <param name="distance">The straight-line distance to cover, in blocks.</param>
    /// <returns>The distance in steps, rounded up; 0 when there is nothing to walk.</returns>
    public static int StepCount(double distance)
        => distance <= 0 ? 0 : (int)Math.Ceiling((distance / StepBlocks) - Slack);

    /// <summary>Gets the position after <paramref name="step"/> of <paramref name="stepCount"/>
    /// equal steps from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">Where the walk starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="step">The step, from 1 to <paramref name="stepCount"/>.</param>
    /// <param name="stepCount">The number of steps, from <see cref="StepCount"/>.</param>
    /// <returns>A new position; the last step is exactly <paramref name="to"/>, whatever rounding
    /// the earlier ones did.</returns>
    public static Vec3d Waypoint(Vec3d from, Vec3d to, int step, int stepCount)
    {
        if (step >= stepCount)
        {
            return to.Clone();
        }

        double t = (double)step / stepCount;
        return new Vec3d(
            from.X + ((to.X - from.X) * t),
            from.Y + ((to.Y - from.Y) * t),
            from.Z + ((to.Z - from.Z) * t));
    }
}
