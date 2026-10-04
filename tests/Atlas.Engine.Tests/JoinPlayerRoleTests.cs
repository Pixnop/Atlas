using Atlas.Engine.Tests.Support;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Atlas.Engine.Tests;

/// <summary>Pins what <c>JoinPlayer</c> documents about roles (issue #193). The role cannot be
/// chosen through the join: the engine puts a dummy-socket player back on the highest-privilege
/// role when its record is created, when it handles the join request and in its own
/// <c>PlayerJoin</c> handler, so a record created beforehand does not survive. A role lowered
/// inside a <c>PlayerJoin</c> handler is the role of everything the server sends the player from
/// then on, which the broadcast scenario shows with a privilege-gated message the lowered joiner
/// must not receive; the order scenario pins the limit of that recipe (a handler subscribed
/// earlier still sees an admin), and the last one that the lowering lasts only until the engine
/// next fetches the player's record. <c>JoinOptions.Role</c> is this recipe, applied by
/// <c>JoinPlayer</c> itself (<see cref="JoinOptionsTests"/>). If the first scenario ever fails, an
/// engine version lets a role be fixed before the join and <c>JoinOptions.Role</c> no longer needs
/// the handler.</summary>
[Trait("Category", "E2E")]
[AtlasWorld(Seed = 939393)]
public class JoinPlayerRoleTests : AtlasScenarioBase
{
    private const string Notice = "atlas-admins-only-notice";

    [AtlasScenario]
    public async Task JoinPlayer_Should_PutThePlayerOnTheHighestRole_When_ARoleRecordExistedBeforeTheJoin()
    {
        // The test player's uid is derived from its name (DummyClientConnector.HandshakePackets).
        EngineProbes.PrecreateRoleRecord(World.Api, "atlas-PrecreatedRole", "PrecreatedRole", "suplayer");

        ITestPlayer player = await World.JoinPlayer("PrecreatedRole");

        Assert.Equal("admin", player.Player.Role.Code);
        Assert.True(player.Player.HasPrivilege(Privilege.controlserver));
    }

    [AtlasScenario]
    public async Task PlayerJoinHandler_Should_GiveTheJoinerTheLowerRole_For_EveryPrivilegeGatedBroadcastAfterIt()
    {
        ITestPlayer admin = await World.JoinPlayer("RoleControlAdm");

        // Handlers run in the order they were subscribed, so the one that lowers the role goes
        // first and the broadcast, which is the "something privileged happens at join" stand-in,
        // second.
        void LowerRole(IServerPlayer joiner)
        {
            if (joiner.PlayerName == "RoleLowered")
            {
                joiner.SetRole("suplayer");
            }
        }

        void BroadcastToAdmins(IServerPlayer joiner)
        {
            foreach (IServerPlayer online in World.Api.World.AllOnlinePlayers.Cast<IServerPlayer>())
            {
                if (online.HasPrivilege(Privilege.controlserver))
                {
                    online.SendMessage(GlobalConstants.GeneralChatGroup, $"{Notice}:{joiner.PlayerName}", EnumChatType.Notification);
                }
            }
        }

        World.Api.Event.PlayerJoin += LowerRole;
        World.Api.Event.PlayerJoin += BroadcastToAdmins;
        ITestPlayer lowered;
        ITestPlayer plain;
        try
        {
            lowered = await World.JoinPlayer("RoleLowered");
            plain = await World.JoinPlayer("RolePlain");
        }
        finally
        {
            World.Api.Event.PlayerJoin -= BroadcastToAdmins;
            World.Api.Event.PlayerJoin -= LowerRole;
        }

        // The lowered player kept its role through the whole join, Playing included.
        Assert.Equal("suplayer", lowered.Player.Role.Code);
        Assert.False(lowered.Player.HasPrivilege(Privilege.controlserver));
        Assert.Equal("admin", plain.Player.Role.Code);

        // It never received the admins-only broadcast of its own join, nor the next joiner's...
        Assert.DoesNotContain(lowered.Client.ChatLines(), line => line.Contains(Notice, StringComparison.Ordinal));

        // ...while the broadcast works: the admin that was already online got both, and the
        // joiner left on the engine's default role got its own.
        Assert.Contains(admin.Client.ChatLines(), line => line == $"{Notice}:RoleLowered");
        Assert.Contains(admin.Client.ChatLines(), line => line == $"{Notice}:RolePlain");
        Assert.Contains(plain.Client.ChatLines(), line => line == $"{Notice}:RolePlain");
    }

    [AtlasScenario]
    public async Task PlayerJoinHandler_Should_SeeTheAdminRole_When_ItWasSubscribedBeforeTheOneThatLowersIt()
    {
        // The order is the limit of the recipe: handlers run in subscription order, and a mod's
        // own PlayerJoin handler was subscribed at boot, before any scenario could lower
        // anything.
        string? seenByEarlyHandler = null;

        void Early(IServerPlayer joiner)
        {
            if (joiner.PlayerName == "OrderLowered")
            {
                seenByEarlyHandler = joiner.Role.Code;
            }
        }

        void LowerRole(IServerPlayer joiner)
        {
            if (joiner.PlayerName == "OrderLowered")
            {
                joiner.SetRole("suplayer");
            }
        }

        World.Api.Event.PlayerJoin += Early;
        World.Api.Event.PlayerJoin += LowerRole;
        ITestPlayer lowered;
        try
        {
            lowered = await World.JoinPlayer("OrderLowered");
        }
        finally
        {
            World.Api.Event.PlayerJoin -= LowerRole;
            World.Api.Event.PlayerJoin -= Early;
        }

        Assert.Equal("admin", seenByEarlyHandler);
        Assert.Equal("suplayer", lowered.Player.Role.Code);
    }

    [AtlasScenario]
    public async Task SetRole_Should_BePutBackToTheHighestRole_When_ALaterPermissionCallReadsTheRecord()
    {
        ITestPlayer player = await World.JoinPlayer("RoleReset");
        player.Player.SetRole("suplayer");
        Assert.Equal("suplayer", player.Player.Role.Code);

        // The engine reads the record through GetOrCreateServerPlayerData, and that read puts a
        // dummy-socket player back on the highest role every time, whoever asks.
        World.Api.Permissions.GrantPrivilege(player.Player.PlayerUID, "atlas-unrelated-privilege");

        Assert.Equal("admin", player.Player.Role.Code);
    }
}
