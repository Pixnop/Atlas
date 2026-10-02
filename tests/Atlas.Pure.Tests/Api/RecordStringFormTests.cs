using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Atlas.Pure.Tests.Api;

/// <summary>The string forms of the public records that hold floating-point numbers are the same
/// on every machine: a decimal point whatever the current culture, so a log pasted from a French
/// machine reads like one from an English machine. Each test runs under fr-FR, where the
/// synthesized record ToString printed <c>MeanMs = 0,005</c>.</summary>
public class RecordStringFormTests
{
    private static readonly CultureInfo French = new("fr-FR");

    [Fact]
    public void PassTimingStats_ToString_Should_UseADecimalPoint_When_TheCultureUsesAComma()
    {
        var stats = new PassTimingStats(0, 1, 2, 3) { MeanMs = 0.005, TotalMs = 15 };

        string text = UnderFrench(stats.ToString);

        Assert.Equal("PassTimingStats { MinMs = 0, MedianMs = 1, P95Ms = 2, MaxMs = 3, MeanMs = 0.005, TotalMs = 15 }", text);
    }

    [Fact]
    public void TickMeasurement_ToString_Should_UseADecimalPoint_When_TheCultureUsesAComma()
    {
        var stats = new PassTimingStats(0, 1, 2, 3) { MeanMs = 1.25, TotalMs = 15 };
        var measurement = new TickMeasurement(12, stats, TimeSpan.FromMilliseconds(1500), 4096);

        string text = UnderFrench(measurement.ToString);

        Assert.Equal(
            "TickMeasurement { Passes = 12, BusyTime = PassTimingStats { MinMs = 0, MedianMs = 1, P95Ms = 2, MaxMs = 3, "
            + "MeanMs = 1.25, TotalMs = 15 }, WallTime = 00:00:01.5000000, AllocatedBytes = 4096 }",
            text);
    }

    [Fact]
    public void SpawnedParticles_ToString_Should_UseADecimalPoint_When_TheCultureUsesAComma()
    {
        var particles = new SpawnedParticles(
            "simple", new SimpleParticleProperties(), new Vec3d(1.5, 2, 3), new Vec3f(0.25f, 0, 1), 2.5f, -16777216);

        string text = UnderFrench(particles.ToString);

        Assert.Equal(
            "SpawnedParticles { ProviderClassName = simple, Provider = Vintagestory.API.Common.SimpleParticleProperties, "
            + "Position = x=1.5, y=2, z=3, Velocity = x=0.25, y=0, z=1, Quantity = 2.5, Color = -16777216 }",
            text);
    }

    [Fact]
    public void Equality_Should_StayValueBased_When_ToStringIsCustomized()
    {
        var stats = new PassTimingStats(0, 1, 2, 3) { MeanMs = 0.005, TotalMs = 15 };
        var same = new PassTimingStats(0, 1, 2, 3) { MeanMs = 0.005, TotalMs = 15 };
        var other = new PassTimingStats(0, 1, 2, 3) { MeanMs = 0.006, TotalMs = 15 };

        Assert.Equal(stats, same);
        Assert.Equal(stats.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(stats, other);
        Assert.Equal(
            new TickMeasurement(1, stats, TimeSpan.Zero, 0), new TickMeasurement(1, same, TimeSpan.Zero, 0));
        Assert.NotEqual(
            new TickMeasurement(1, stats, TimeSpan.Zero, 0), new TickMeasurement(1, other, TimeSpan.Zero, 0));
    }

    private static string UnderFrench(Func<string> format)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = French;
        try
        {
            // Without ICU the culture silently behaves like the invariant one and the test would
            // pass for nothing: prove the comma is really in force first.
            Assert.Equal("0,5", 0.5.ToString(CultureInfo.CurrentCulture));
            return format();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
