using Atlas.XUnit;
using Vintagestory.API.Common;

namespace Atlas.TwoBuilds.Scenarios;

/// <summary>What the two scenario classes share: reading which build of BindingFixtureMod the
/// engine actually bound, off the loaded mod system.</summary>
public abstract class TwoBuildsScenarioBase : AtlasScenarioBase
{
    protected string RunningBuild()
    {
        // By reflection: the loaded type is whichever assembly the engine bound, so a
        // compile-time reference would say nothing about it.
        ModSystem system = World.Api.ModLoader.GetModSystem("BindingFixtureMod.BindingFixtureModSystem");
        return (string)system.GetType().GetProperty("Build")!.GetValue(system)!;
    }
}
