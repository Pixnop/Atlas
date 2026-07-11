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
    /// <param name="restartWorld">The <see cref="AtlasScenarioAttribute.RestartWorld"/> flag.</param>
    /// <param name="strictIsolation">The <see cref="AtlasScenarioAttribute.StrictIsolation"/> flag.</param>
    /// <returns>The single isolation mode the scenario asked for.</returns>
    /// <exception cref="AtlasSetupException">Thrown when more than one of the three world flags
    /// is set (they contradict pairwise: FreshWorld demands a brand-new world, RollbackWorld
    /// restores the existing host's world snapshot, RestartWorld carries the current world over
    /// into a rebooted host), or when <paramref name="strictIsolation"/> accompanies anything but
    /// <paramref name="rollbackWorld"/> (only a rollback request can degrade, so there is no
    /// contract to be strict about; a restart in particular either works or fails the scenario
    /// hard).</exception>
    public static WorldIsolation Resolve(
        string scenarioDisplayName, bool freshWorld, bool rollbackWorld, bool restartWorld, bool strictIsolation)
    {
        if (freshWorld && rollbackWorld)
        {
            throw new AtlasSetupException(
                $"'{scenarioDisplayName}' sets both FreshWorld and RollbackWorld; they contradict " +
                "(FreshWorld recycles the whole host, RollbackWorld restores the existing host's " +
                "world snapshot). Pick one.");
        }

        if (freshWorld && restartWorld)
        {
            throw new AtlasSetupException(
                $"'{scenarioDisplayName}' sets both FreshWorld and RestartWorld; they contradict " +
                "(FreshWorld throws the world away for a brand-new one, RestartWorld reboots the " +
                "host and carries the current world over). Pick one.");
        }

        if (rollbackWorld && restartWorld)
        {
            throw new AtlasSetupException(
                $"'{scenarioDisplayName}' sets both RollbackWorld and RestartWorld; they contradict " +
                "(RollbackWorld restores the world snapshot on the same running host, RestartWorld " +
                "reboots the host and carries the current world over). Pick one.");
        }

        if (strictIsolation && restartWorld)
        {
            throw new AtlasSetupException(
                $"'{scenarioDisplayName}' sets StrictIsolation with RestartWorld. A restart either " +
                "works or fails the scenario hard (there is no silent fallback to be strict " +
                "about); drop StrictIsolation.");
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

        if (restartWorld)
        {
            return WorldIsolation.RestartWorld;
        }

        return rollbackWorld ? WorldIsolation.RollbackWorld : WorldIsolation.SharedWorld;
    }
}
