using Xunit;
using Xunit.Sdk;

namespace Atlas.XUnit;

/// <summary>Marks a test method as a parameterized Atlas scenario: the theory-style counterpart
/// to <see cref="AtlasScenarioAttribute"/>. Combine with <c>[InlineData]</c>, <c>[MemberData]</c>
/// or any other xUnit <c>DataAttribute</c>; each data row runs as its own scenario on the
/// embedded game server's game thread, with the row's values in its display name.</summary>
/// <remarks>The settings below mirror <see cref="AtlasScenarioAttribute"/> exactly and apply per
/// data row (each row is a full scenario of its own, so e.g. <see cref="FreshWorld"/> recycles
/// the host before every row). Kept a sibling of <see cref="AtlasScenarioAttribute"/> rather than
/// a subclass, the same way xUnit keeps <see cref="TheoryAttribute"/> beside
/// <see cref="FactAttribute"/>, so each attribute binds unambiguously to its own discoverer.</remarks>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Atlas.XUnit.Internal.AtlasTheoryDiscoverer", "Atlas.XUnit")]
public sealed class AtlasTheoryAttribute : TheoryAttribute
{
    /// <summary>Gets or sets a value indicating whether the class host is recycled before each
    /// data row runs, giving the row a fresh world instead of the one shared by the test class.
    /// See <see cref="AtlasScenarioAttribute.FreshWorld"/>.</summary>
    public bool FreshWorld { get; set; }

    /// <summary>Gets or sets a value indicating whether the class host's world is rolled back to
    /// its snapshot before each data row runs, the cheap alternative to <see cref="FreshWorld"/>.
    /// See <see cref="AtlasScenarioAttribute.RollbackWorld"/> for what a rollback does and does
    /// not restore, and for the fail-closed fallback semantics.</summary>
    public bool RollbackWorld { get; set; }

    /// <summary>Gets or sets a value indicating whether the class host is genuinely restarted
    /// before each data row runs: graceful shutdown, then a replacement host booted against the
    /// persisted save, so every row runs on a world that survived a real save/load round trip.
    /// See <see cref="AtlasScenarioAttribute.RestartWorld"/> for the cost, the composition with
    /// prebuilt saves, the fail-hard (never fall back) rules and the joined-players guard; they
    /// all apply per row.</summary>
    public bool RestartWorld { get; set; }

    /// <summary>Gets or sets a value indicating whether a degraded <see cref="RollbackWorld"/>
    /// request fails the data row instead of silently falling back to a full host recycle.
    /// See <see cref="AtlasScenarioAttribute.StrictIsolation"/> for the contract; the same
    /// mutual-exclusion rules apply per row (only meaningful with <see cref="RollbackWorld"/>,
    /// a setup error otherwise).</summary>
    public bool StrictIsolation { get; set; }

    /// <summary>Gets or sets the maximum time, in milliseconds, each data row is allowed to run.
    /// Enforced by an off-thread watchdog, not xUnit's own timeout path: see
    /// <see cref="AtlasScenarioAttribute.TimeoutMs"/> for why.</summary>
    public int TimeoutMs { get; set; } = 60_000;
}
