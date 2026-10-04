using Atlas.Internal.Player;
using Vintagestory.API.MathTools;

namespace Atlas.Pure.Tests.Player;

/// <summary>The geometry behind <c>ITestPlayer.WalkTo</c>: how many fixed steps a straight line
/// takes and where each one lands. The step count rounds up, so a distance that is a whole
/// number of steps in decimal does not take an extra one because of binary rounding, and the
/// last step lands exactly on the target whatever the earlier ones did.</summary>
public class WalkPathTests
{
    [Fact]
    public void StepCount_Should_BeZero_When_ThereIsNothingToWalk()
    {
        Assert.Equal(0, WalkPath.StepCount(0));
        Assert.Equal(0, WalkPath.StepCount(-1));
    }

    [Theory]
    [InlineData(0.05, 1)]
    [InlineData(0.2, 1)]
    [InlineData(0.21, 2)]
    [InlineData(0.6, 3)]
    [InlineData(0.8, 4)]
    [InlineData(1.0, 5)]
    [InlineData(1.4, 7)]
    [InlineData(10.0, 50)]
    [InlineData(10.01, 51)]
    public void StepCount_Should_RoundUpToWholeSteps(double distance, int expected)
    {
        Assert.Equal(expected, WalkPath.StepCount(distance));
    }

    [Fact]
    public void Waypoint_Should_LandExactlyOnTheTarget_When_ItIsTheLastStep()
    {
        var from = new Vec3d(0.1, 3, 0.7);
        var to = new Vec3d(7.3, 3, 5.9);
        int steps = WalkPath.StepCount(from.DistanceTo(to));

        Vec3d last = WalkPath.Waypoint(from, to, steps, steps);

        Assert.Equal(to.X, last.X);
        Assert.Equal(to.Y, last.Y);
        Assert.Equal(to.Z, last.Z);
    }

    [Fact]
    public void Waypoint_Should_AdvanceInEqualSteps_No_LongerThanTheFixedStep()
    {
        var from = new Vec3d(10, 3, 10);
        var to = new Vec3d(13, 4, 6);
        int steps = WalkPath.StepCount(from.DistanceTo(to));

        Vec3d previous = from;
        for (int step = 1; step <= steps; step++)
        {
            Vec3d next = WalkPath.Waypoint(from, to, step, steps);
            Assert.InRange(previous.DistanceTo(next), 0.0, WalkPath.StepBlocks + 1e-9);
            previous = next;
        }

        Assert.Equal(0, previous.DistanceTo(to), precision: 9);
    }

    [Fact]
    public void Waypoint_Should_StayOnTheStraightLine()
    {
        var from = new Vec3d(0, 0, 0);
        var to = new Vec3d(4, 0, 2);
        int steps = WalkPath.StepCount(from.DistanceTo(to));

        for (int step = 1; step <= steps; step++)
        {
            Vec3d point = WalkPath.Waypoint(from, to, step, steps);
            Assert.Equal(point.X / 2, point.Z, precision: 9);
            Assert.Equal(0, point.Y);
        }
    }
}
