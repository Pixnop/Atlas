using Atlas.Api;
using Atlas.XUnit;

namespace Atlas.Engine.Tests;

/// <summary>Documents the per-class-host reality for <c>JoinPlayer</c>: two scenarios on the SAME
/// shared class host (no <c>FreshWorld = true</c>) contend for the world's single dummy-network
/// player slot, so only the first one to run can join; the second observes
/// <see cref="AtlasSetupException"/>.</summary>
/// <remarks>xUnit does not guarantee method execution order within a class, so which scenario runs
/// first (and therefore which one "wins" the join) is not fixed. Both scenarios call the private
/// <see cref="EnsureJoined"/> helper, which tracks whether THIS call performed the join or found
/// the slot already taken by the other scenario, and asserts accordingly - the assertions hold
/// regardless of which scenario xUnit happens to run first. This is deliberately kept in its own
/// class (mirroring <see cref="DeadHostFailFastTests"/>'s isolation reasoning) so a scenario here
/// can never race a scenario from an unrelated test class over the same shared host.</remarks>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 912)]
public class SharedHostJoinGuardTests : AtlasScenarioBase
{
    private static readonly object Gate = new();
    private static bool _slotClaimed;

    [AtlasScenario]
    public async Task Scenario_Should_JoinOrObserveGuard_When_RunFirst() => await EnsureJoined();

    [AtlasScenario]
    public async Task Scenario_Should_JoinOrObserveGuard_When_RunSecond() => await EnsureJoined();

    /// <summary>Joins the shared class host's single player slot if nobody has claimed it yet in
    /// this run, or asserts the documented guard message if another scenario already has.</summary>
    private async Task EnsureJoined()
    {
        bool iAmFirst;
        lock (Gate)
        {
            iAmFirst = !_slotClaimed;
            _slotClaimed = true;
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
            Assert.Contains("share the resulting ITestPlayer across scenarios via a field", ex.Message);
        }
    }
}
