using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo(
    "Atlas Tick Timing Fixture",
    "ticktimingfixture",
    Version = "0.1.0",
    Side = "Server",
    Description = "Test fixture for Atlas's per-pass tick timing (IWorldSession.MeasureTicks): a tick listener with a known, deliberately expensive cost.")]

namespace TickTimingFixtureMod;

/// <summary>A miniature of a mod whose tick listener does real, CPU-bound work: registers one
/// game-tick listener that busy-spins for a fixed, known duration every tick, so a
/// <c>MeasureTicks</c> window has a cost it can be asserted to have seen. No chat command, no
/// toggle: this mod is staged explicitly by one test class (never by every host, like the other
/// fixture mods in this folder), so "always on" is the whole fixture.</summary>
public sealed class TickTimingFixtureModSystem : ModSystem
{
    /// <summary>How long <see cref="OnTick"/> spins for, every tick. Chosen well above the
    /// noise floor an idle world measures (a fraction of a millisecond per pass, see
    /// docs/specs/2026-09-23-tick-timing.md) and comfortably under the engine's default ~33ms
    /// pacing budget, so the spin does not itself trigger the "Server overloaded" warning.</summary>
    public const int SpinMilliseconds = 20;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api) => api.Event.RegisterGameTickListener(OnTick, 1);

    /// <summary>Busy-spins for <see cref="SpinMilliseconds"/>: deliberate CPU burn, not
    /// <c>Thread.Sleep</c>, so the cost shows up as engine-measured busy time rather than as
    /// pacing slack the engine's own sleep would absorb.</summary>
    /// <param name="dt">Unused: the spin duration is fixed, not scaled by delta time.</param>
    private static void OnTick(float dt)
    {
        var spin = Stopwatch.StartNew();
        while (spin.ElapsedMilliseconds < SpinMilliseconds)
        {
        }
    }
}
