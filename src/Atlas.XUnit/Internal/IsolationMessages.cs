using System.Globalization;
using Atlas.Internal.Rollback;

namespace Atlas.XUnit.Internal;

/// <summary>Formats the human-facing messages of a degraded rollback: the report attached to the
/// scenario's own test output, and the failure message of a strict-isolation scenario. Pure:
/// kept out of the invoker so the wording is unit-testable without xUnit machinery.</summary>
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

    private static string FormatSeconds(TimeSpan duration)
        => duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
}
