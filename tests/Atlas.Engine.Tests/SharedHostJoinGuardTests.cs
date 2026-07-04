using Atlas.Api;
using Atlas.XUnit;

namespace Atlas.Engine.Tests;

/// <summary>Documents the per-class-host reality for <c>JoinPlayer</c>: two scenarios on the SAME
/// shared class host (no <c>FreshWorld = true</c>) share one world, so a player joined by the
/// first scenario is still connected when the second runs. Rejoining under the same name throws
/// <see cref="AtlasSetupException"/> (the server would kick the first player as a duplicate
/// account); joining under a fresh name succeeds, since concurrent players are supported.</summary>
/// <remarks>xUnit does not guarantee method execution order within a class, so which scenario runs
/// first (and therefore which one performs the initial join) is not fixed. Both scenarios call the
/// private <see cref="EnsureJoined"/> helper, which tracks whether THIS call performed the join or
/// found the name already taken by the other scenario, and asserts accordingly - the assertions
/// hold regardless of which scenario xUnit happens to run first. This is deliberately kept in its
/// own class (mirroring <see cref="DeadHostFailFastTests"/>'s isolation reasoning) so a scenario
/// here can never race a scenario from an unrelated test class over the same shared host.</remarks>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 912)]
public class SharedHostJoinGuardTests : AtlasScenarioBase
{
    private static readonly object Gate = new();
    private static bool _nameClaimed;

    [AtlasScenario]
    public async Task Scenario_Should_JoinOrObserveGuard_When_RunFirst() => await EnsureJoined();

    [AtlasScenario]
    public async Task Scenario_Should_JoinOrObserveGuard_When_RunSecond() => await EnsureJoined();

    /// <summary>Joins the shared class host's world as "SharedHostPlayer" if nobody has claimed
    /// that name yet in this run; otherwise asserts the documented duplicate-name guard, then
    /// shows the supported way out by joining under a different name.</summary>
    private async Task EnsureJoined()
    {
        bool iAmFirst;
        lock (Gate)
        {
            iAmFirst = !_nameClaimed;
            _nameClaimed = true;
        }

        if (iAmFirst)
        {
            ITestPlayer player = await World.JoinPlayer("SharedHostPlayer");
            Assert.NotNull(player.Entity);
        }
        else
        {
            AtlasSetupException ex = await Assert.ThrowsAsync<AtlasSetupException>(
                () => World.JoinPlayer("SharedHostPlayer"));

            Assert.Contains("already joined this class's world", ex.Message);
            Assert.Contains("FreshWorld = true", ex.Message);
            Assert.Contains("share it via a field", ex.Message);

            // The name is taken, not the world: a fresh name joins the same world just fine.
            // (Kept within the engine's 16-character player name limit.)
            ITestPlayer second = await World.JoinPlayer("SharedHostP2");
            Assert.NotNull(second.Entity);
        }
    }
}
