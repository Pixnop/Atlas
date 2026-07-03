using Atlas.Api;

namespace Atlas.XUnit;

/// <summary>Base class for scenario test classes driven by <see cref="AtlasScenarioAttribute"/>.</summary>
public abstract class AtlasScenarioBase
{
    /// <summary>Gets the world surface for the current scenario; assigned by the Atlas invoker
    /// before the scenario body runs.</summary>
    protected internal IWorldSession World { get; internal set; } = null!;
}
