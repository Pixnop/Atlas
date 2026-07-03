using Xunit;
using Xunit.Sdk;

namespace Atlas.XUnit;

/// <summary>Marks a test method as an Atlas scenario, run on the embedded game server's game thread.</summary>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Atlas.XUnit.Internal.AtlasScenarioDiscoverer", "Atlas.XUnit")]
public sealed class AtlasScenarioAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="AtlasScenarioAttribute"/> class.</summary>
    public AtlasScenarioAttribute() => Timeout = 60_000;

    /// <summary>Gets or sets a value indicating whether the class host is recycled before this
    /// scenario runs, giving it a fresh world instead of the one shared by the test class.</summary>
    public bool FreshWorld { get; set; }

    /// <summary>Gets or sets the maximum time, in milliseconds, the scenario is allowed to run.</summary>
    /// <remarks>Maps onto <see cref="FactAttribute.Timeout"/>; the watchdog that enforces it against
    /// the game thread is wired up separately.</remarks>
    public int TimeoutMs
    {
        get => Timeout;
        set => Timeout = value;
    }
}
