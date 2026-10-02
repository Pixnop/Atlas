using Atlas.XUnit;
using Xunit;

namespace Atlas.Scratch.Scenarios;

/// <summary>What the two probe classes share: one scenario that boots the class host, ticks it
/// once and, when <see cref="FailVariable"/> names this class, fails with the host still
/// healthy. Two classes, so a run exercises both ends of a host's life: whichever runs first is
/// handed off to the second, and the second is released when the process exits. Each class has
/// its own world seed, so the "Using world seed" line in a kept server-main.log says which class
/// the directory belongs to.</summary>
public abstract class ProbeScenariosBase : AtlasScenarioBase
{
    /// <summary>The variable that turns a probe red: the simple name of the class that fails.</summary>
    public const string FailVariable = "ATLAS_SCRATCH_PROBE_FAIL";

    /// <summary>Boots the host, ticks it, and fails when <see cref="FailVariable"/> names
    /// <paramref name="className"/>.</summary>
    /// <param name="className">The simple name of the calling class.</param>
    /// <returns>A task that completes when the scenario is done.</returns>
    protected async Task TickAndMaybeFailAsync(string className)
    {
        await World.Ticks(1);
        Assert.NotEqual(className, Environment.GetEnvironmentVariable(FailVariable));
    }
}
