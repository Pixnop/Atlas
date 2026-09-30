using Vintagestory.API.Common;

[assembly: ModInfo(
    "Atlas Binding Fixture",
    "bindingfixture",
    Version = "0.1.0",
    Side = "Server",
    Description = "Test fixture for Atlas's staged-mod binding check: two builds of one assembly identity that differ only in what Build reports.")]

namespace BindingFixtureMod;

/// <summary>A mod whose only behavior is to say which of its two builds is running. Compiled
/// twice under the same assembly identity (see BindingFixtureMod.Alpha.csproj); a test reads
/// <see cref="Build"/> off the loaded system to see which build the engine actually bound.</summary>
public sealed class BindingFixtureModSystem : ModSystem
{
#if BETA_BUILD
    public const string BuildName = "beta";
#else
    public const string BuildName = "alpha";
#endif

    /// <summary>Gets the build this loaded assembly was compiled as. An instance member on
    /// purpose: a <c>const</c> read from the test project would be inlined at its own compile
    /// time, and would say nothing about which assembly the engine really loaded.</summary>
    public string Build => BuildName;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
}
