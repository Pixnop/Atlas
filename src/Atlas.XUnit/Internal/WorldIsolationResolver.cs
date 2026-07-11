using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>Resolves the <see cref="AtlasScenarioAttribute"/> isolation flags into one
/// <see cref="WorldIsolation"/> mode, rejecting the contradictory combinations. Pure: kept out of
/// the invoker so the mapping is unit-testable without xUnit machinery.</summary>
internal static class WorldIsolationResolver
{
    /// <summary>Resolves the isolation mode for one scenario.</summary>
    /// <param name="scenarioDisplayName">The scenario's display name, for the error message.</param>
    /// <param name="freshWorld">The <see cref="AtlasScenarioAttribute.FreshWorld"/> flag.</param>
    /// <param name="rollbackWorld">The <see cref="AtlasScenarioAttribute.RollbackWorld"/> flag.</param>
    /// <param name="strictIsolation">The <see cref="AtlasScenarioAttribute.StrictIsolation"/> flag.</param>
    /// <returns>The single isolation mode the scenario asked for.</returns>
    /// <exception cref="AtlasSetupException">Thrown when both world flags are set (they
    /// contradict: FreshWorld demands a brand-new host while RollbackWorld reuses the existing
    /// host's world snapshot), or when <paramref name="strictIsolation"/> is set without
    /// <paramref name="rollbackWorld"/> (only a rollback request can degrade, so there is no
    /// contract to be strict about).</exception>
    public static WorldIsolation Resolve(
        string scenarioDisplayName, bool freshWorld, bool rollbackWorld, bool strictIsolation)
    {
        if (freshWorld && rollbackWorld)
        {
            throw new AtlasSetupException(
                $"'{scenarioDisplayName}' sets both FreshWorld and RollbackWorld; they contradict " +
                "(FreshWorld recycles the whole host, RollbackWorld restores the existing host's " +
                "world snapshot). Pick one.");
        }

        if (strictIsolation && !rollbackWorld)
        {
            throw new AtlasSetupException(
                $"'{scenarioDisplayName}' sets StrictIsolation without RollbackWorld. Only a " +
                "RollbackWorld request can degrade to a full host recycle, so strictness has " +
                "nothing to enforce; add RollbackWorld = true or drop StrictIsolation.");
        }

        if (freshWorld)
        {
            return WorldIsolation.FreshWorld;
        }

        return rollbackWorld ? WorldIsolation.RollbackWorld : WorldIsolation.SharedWorld;
    }
}
