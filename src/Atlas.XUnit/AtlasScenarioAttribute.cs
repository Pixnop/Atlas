using Xunit;
using Xunit.Sdk;

namespace Atlas.XUnit;

/// <summary>Marks a test method as an Atlas scenario, run on the embedded game server's game thread.</summary>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Atlas.XUnit.Internal.AtlasScenarioDiscoverer", "Atlas.XUnit")]
public sealed class AtlasScenarioAttribute : FactAttribute
{
    /// <summary>Gets or sets a value indicating whether the class host is recycled before this
    /// scenario runs, giving it a fresh world instead of the one shared by the test class.</summary>
    public bool FreshWorld { get; set; }

    /// <summary>Gets or sets the maximum time, in milliseconds, the scenario is allowed to run.</summary>
    /// <remarks>Deliberately does NOT map onto <see cref="FactAttribute.Timeout"/>: xUnit's own
    /// timeout path posts its <c>TestTimeoutException</c> continuation back through
    /// <c>SynchronizationContext.Current</c>, which for an Atlas scenario is the game thread's queue.
    /// If the game thread is the one that is stuck, that continuation never drains and the test hangs
    /// forever instead of failing at the timeout. This value flows to <c>AtlasTestCase</c> as plain
    /// data and is enforced by an off-thread <c>Watchdog</c> instead.</remarks>
    public int TimeoutMs { get; set; } = 60_000;
}
