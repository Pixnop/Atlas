using Atlas.Api;
using Xunit;

namespace Atlas.XUnit;

/// <summary>Base class for scenario test classes driven by <see cref="AtlasScenarioAttribute"/>.</summary>
/// <remarks>Declares <see cref="AtlasClassLifetime"/> as its class fixture, so the class's server
/// is released when the class ends. A derived class does not have to do anything about it: its
/// own constructor and its own <c>IClassFixture</c> declarations are unaffected.</remarks>
public abstract class AtlasScenarioBase : IClassFixture<AtlasClassLifetime>
{
    /// <summary>Gets the world surface for the current scenario; assigned by the Atlas invoker
    /// before the scenario body runs.</summary>
    protected internal IWorldSession World { get; internal set; } = null!;
}
