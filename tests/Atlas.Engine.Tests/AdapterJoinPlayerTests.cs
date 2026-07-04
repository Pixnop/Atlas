using Atlas.XUnit;

namespace Atlas.Engine.Tests;

/// <summary>Proves <c>JoinPlayer</c> works through the <see cref="AtlasScenarioAttribute"/> adapter
/// path (a class deriving <see cref="AtlasScenarioBase"/>, run through <c>HostRegistry</c>'s shared
/// class host), not just through the lower-level <c>ServerHost.RunScenarioAsync</c> path the other
/// <c>TestPlayerTests</c> cover.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 911)]
public class AdapterJoinPlayerTests : AtlasScenarioBase
{
    [AtlasScenario(FreshWorld = true)]
    public async Task JoinPlayer_Should_SpawnPlayerPresentInWorld_When_JoinedThroughAdapter()
    {
        ITestPlayer player = await World.JoinPlayer("AdapterPlayer");

        Assert.NotNull(player.Entity);
        IReadOnlyList<Vintagestory.API.Common.Entities.Entity> nearby = World.EntitiesIn(player.Position.Area(16));
        Assert.Contains(nearby, e => e.EntityId == player.Entity.EntityId);
    }
}
