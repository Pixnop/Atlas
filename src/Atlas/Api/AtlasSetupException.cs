namespace Atlas.Api;

/// <summary>Thrown when Atlas cannot prepare or hand over the test environment.</summary>
/// <remarks>Unrelated setup failures share it, in these groups. Environment preparation: a
/// missing or wrong VINTAGE_STORY install, mod or data-file paths that do not resolve, a staging
/// copy that fails, a world save or schematic file the engine cannot load, a boot where the
/// Atlas bridge mod never started. Declaration errors: a scenario class that does not
/// derive from <c>AtlasScenarioBase</c>, or contradictory isolation flags on
/// <c>[AtlasScenario]</c> (the three world modes contradict pairwise, and StrictIsolation only
/// pairs with RollbackWorld). Calls that cannot proceed: joining a test player under a name
/// already joined in this world, or restarting a class that has joined players. Engine drift:
/// a wait on an engine signal that never settles, where the diagnosis is that the engine's
/// layout moved relative to this Atlas build rather than that the wait was short (the join's
/// <c>Playing</c> transition, the background server-assets build, the release of joined-name
/// claims after a rollback); an engine member, field or enum value that is not where this Atlas
/// build expects it on the running game version; or a game version below the supported
/// floor.</remarks>
public sealed class AtlasSetupException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AtlasSetupException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public AtlasSetupException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AtlasSetupException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception.</param>
    public AtlasSetupException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
