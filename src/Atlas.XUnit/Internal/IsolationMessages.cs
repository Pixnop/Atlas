using System.Globalization;
using Atlas.Internal.Rollback;

namespace Atlas.XUnit.Internal;

/// <summary>Formats the human-facing messages of world isolation: the report attached to the
/// scenario's own test output when a rollback degraded, the failure message of a
/// strict-isolation scenario, and the hard failures of a RestartWorld request. Pure: kept out
/// of the invoker and the registry so the wording is unit-testable without xUnit machinery.</summary>
internal static class IsolationMessages
{
    /// <summary>Formats the report attached to a scenario's test output when its rollback
    /// request degraded to a full host recycle.</summary>
    /// <param name="reason">The structured degrade reason.</param>
    /// <param name="detail">The one-line failure detail ("ExceptionType: message").</param>
    /// <param name="recycleCost">Wall-clock cost of the fallback recycle.</param>
    /// <returns>The single-line report.</returns>
    public static string DegradeReport(RollbackDegradeReason reason, string detail, TimeSpan recycleCost)
        => "[Atlas] world isolation degraded: RollbackWorld fell back to a full host recycle " +
           $"(cost {FormatSeconds(recycleCost)}). Reason: {RollbackDegrade.Describe(reason)}. {detail}";

    /// <summary>Formats the report attached to a scenario's test output when its RestartWorld
    /// request completed. Unlike a degrade this is not a warning: the restart is explicit and
    /// paid by exactly this scenario, but its cost is invisible in the scenario's own reported
    /// duration (the restart happens before the timed body), so the line makes it visible.</summary>
    /// <param name="cost">Wall-clock cost of the restart (shutdown + harvest + boot).</param>
    /// <returns>The single-line report.</returns>
    public static string RestartReport(TimeSpan cost)
        => "[Atlas] world restarted: RestartWorld shut the outgoing host down, harvested its save " +
           $"and booted a replacement (cost {FormatSeconds(cost)}, paid outside the scenario's " +
           "reported duration).";

    /// <summary>Formats the failure message of a strict-isolation scenario whose rollback
    /// request degraded.</summary>
    /// <param name="scenarioDisplayName">The scenario's display name.</param>
    /// <param name="reason">The structured degrade reason.</param>
    /// <param name="detail">The one-line failure detail ("ExceptionType: message").</param>
    /// <returns>The failure message.</returns>
    public static string StrictFailure(string scenarioDisplayName, RollbackDegradeReason reason, string detail)
        => $"'{scenarioDisplayName}' requested StrictIsolation and its RollbackWorld isolation " +
           $"degraded to a full host recycle. Reason: {RollbackDegrade.Describe(reason)}. {detail} " +
           "The host was recycled, so later scenarios of the class still get a clean world; fix " +
           "the degrade cause or drop StrictIsolation to accept the slower fallback.";

    /// <summary>Formats the failure message of a RestartWorld scenario whose graceful shutdown
    /// did not leave a persisted world save to boot the replacement host against.</summary>
    /// <param name="testClassName">The scenario class's display name.</param>
    /// <param name="expectedSavePath">The save path the harvest expected to find.</param>
    /// <returns>The failure message.</returns>
    public static string RestartHarvestFailure(string testClassName, string expectedSavePath)
        => $"RestartWorld for '{testClassName}' failed: the outgoing host's graceful shutdown did " +
           $"not persist a world save at '{expectedSavePath}', so there is nothing to boot the " +
           "replacement host against. A restart never falls back silently; the scenario fails " +
           "instead, and the class's next scenario boots a new host from its attributes.";

    /// <summary>Formats the failure message of a RestartWorld request on a host with joined
    /// test players, whose connections would die with the host.</summary>
    /// <param name="testClassName">The scenario class's display name.</param>
    /// <returns>The failure message.</returns>
    public static string RestartPlayersJoinedFailure(string testClassName)
        => $"RestartWorld is not supported on a host with joined test players: '{testClassName}' " +
           "has joined test players, and their connections die with the host, so they would not " +
           "survive the restart. Rather than silently dropping them, the scenario fails; re-join " +
           "the players after the restart, or use [AtlasScenario(FreshWorld = true)] when the " +
           "carried-over world is not actually needed.";

    /// <summary>Formats a wall-clock duration as seconds with one decimal, invariant culture
    /// (also used by <see cref="IsolationTally"/> so the summary line and the per-test reports
    /// spell costs identically).</summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>The formatted duration, e.g. "7.1 s".</returns>
    internal static string FormatSeconds(TimeSpan duration)
        => duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
}
