using Vintagestory.API.MathTools;

namespace Atlas.Internal.Player;

/// <summary>The yaw and pitch that make an entity look from one point at another, in the
/// convention the engine reads them back with (<c>EntityPos.GetViewVector</c>, which the server
/// traces a player's block selection along every tick).</summary>
internal static class AimAngles
{
    /// <summary>Works out the angles that aim from <paramref name="eye"/> at
    /// <paramref name="target"/>.</summary>
    /// <param name="eye">Where the look starts.</param>
    /// <param name="target">What it points at.</param>
    /// <param name="yaw">The yaw, in radians within one turn.</param>
    /// <param name="pitch">The pitch, in radians: positive looks up.</param>
    /// <returns><see langword="false"/> when the two points are the same, which has no direction;
    /// both angles are then 0.</returns>
    public static bool TryToward(Vec3d eye, Vec3d target, out float yaw, out float pitch)
    {
        double dx = target.X - eye.X;
        double dy = target.Y - eye.Y;
        double dz = target.Z - eye.Z;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length == 0)
        {
            yaw = 0;
            pitch = 0;
            return false;
        }

        // GetViewVector(pitch, yaw) is (-cos(p) sin(y), sin(p), -cos(p) cos(y)).
        pitch = (float)Math.Asin(dy / length);
        double turn = Math.Atan2(-dx, -dz);
        yaw = (float)(turn < 0 ? turn + (2 * Math.PI) : turn);
        return true;
    }
}
