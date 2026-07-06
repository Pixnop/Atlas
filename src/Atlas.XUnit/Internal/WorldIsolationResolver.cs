using Atlas.Api;

namespace Atlas.XUnit.Internal;

/// <summary>Resolves the two <see cref="AtlasScenarioAttribute"/> isolation flags into one
/// <see cref="WorldIsolation"/> mode, rejecting the contradictory combination. Pure: kept out of
/// the invoker so the mapping is unit-testable without xUnit machinery.</summary>
internal static class WorldIsolationResolver
{
    /// <summary>Resolves the isolation mode for one scenario.</summary>
    /// <param name="scenarioDisplayName">The scenario's display name, for the error message.</param>
    /// <param name="freshWorld">The <see cref="AtlasScenarioAttribute.FreshWorld"/> flag.</param>
    /// <param name="rollbackWorld">The <see cref="AtlasScenarioAttribute.RollbackWorld"/> flag.</param>
    /// <returns>The single isolation mode the scenario asked for.</returns>
    /// <exception cref="AtlasSetupException">Thrown when both flags are set: they contradict.
    /// FreshWorld demands a brand-new host while RollbackWorld reuses the existing host's world
    /// snapshot, so a scenario cannot meaningfully ask for both.</exception>
    public static WorldIsolation Resolve(string scenarioDisplayName, bool freshWorld, bool rollbackWorld)
    {
        if (freshWorld && rollbackWorld)
        {
            throw new AtlasSetupException(
                $"'{scenarioDisplayName}' sets both FreshWorld and RollbackWorld; they contradict " +
                "(FreshWorld recycles the whole host, RollbackWorld restores the existing host's " +
                "world snapshot). Pick one.");
        }

        if (freshWorld)
        {
            return WorldIsolation.FreshWorld;
        }

        return rollbackWorld ? WorldIsolation.RollbackWorld : WorldIsolation.SharedWorld;
    }
}
