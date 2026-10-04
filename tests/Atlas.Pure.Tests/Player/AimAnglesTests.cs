using Atlas.Internal.Player;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Atlas.Pure.Tests.Player;

/// <summary>The yaw and pitch <c>ITestPlayer.LookAt</c> aims with. The engine reads them back
/// through <c>EntityPos.GetViewVector</c> when it traces the player's selection every tick, so
/// that is the function each case closes the loop with: the vector built from the returned
/// angles has to point from the eye to the target.</summary>
public class AimAnglesTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 1, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(3, -2, 4)]
    [InlineData(-3, 2, -4)]
    [InlineData(2.5, 0.4, -7)]
    [InlineData(-0.5, -1.7, 0.9)]
    public void TryToward_Should_BuildAViewVectorTowardTheTarget(double dx, double dy, double dz)
    {
        var eye = new Vec3d(100, 5.6, -40);
        var target = new Vec3d(eye.X + dx, eye.Y + dy, eye.Z + dz);

        Assert.True(AimAngles.TryToward(eye, target, out float yaw, out float pitch));

        Vec3f view = EntityPos.GetViewVector(pitch, yaw);
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Assert.Equal(dx / length, view.X, precision: 4);
        Assert.Equal(dy / length, view.Y, precision: 4);
        Assert.Equal(dz / length, view.Z, precision: 4);
    }

    [Fact]
    public void TryToward_Should_KeepYawInOneTurn()
    {
        foreach ((double dx, double dz) in new[] { (1.0, 1.0), (-1.0, 1.0), (-1.0, -1.0), (1.0, -1.0) })
        {
            Assert.True(AimAngles.TryToward(new Vec3d(0, 0, 0), new Vec3d(dx, 0.3, dz), out float yaw, out _));
            Assert.InRange(yaw, 0f, 2 * MathF.PI);
        }
    }

    [Fact]
    public void TryToward_Should_ReportNothingToAimAt_When_TheEyeIsOnTheTarget()
    {
        var point = new Vec3d(1, 2, 3);

        Assert.False(AimAngles.TryToward(point, point.Clone(), out float yaw, out float pitch));
        Assert.Equal(0f, yaw);
        Assert.Equal(0f, pitch);
    }
}
