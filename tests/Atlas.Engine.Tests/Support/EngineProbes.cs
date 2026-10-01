using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.Internal.Player;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Engine.Tests.Support;

/// <summary>The few engine internals the client-observation scenarios touch directly. They are
/// kept out of the test methods on purpose: every member is <see cref="MethodImplOptions.NoInlining"/>
/// and only called once a host has booted, because a method that names a <c>VintagestoryLib</c>
/// type is resolved when it is compiled, and that assembly only becomes loadable through the
/// host's own late resolver (see the reference in the project file).</summary>
internal static class EngineProbes
{
    /// <summary>Counts the live entries of the engine's game-tick listener list, where every
    /// <c>RegisterGameTickListener</c> of the server API ends up.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>The number of registered listeners.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int TickListeners(ICoreServerAPI api)
    {
        EventManager manager = ((ServerMain)api.World).EventManager;
        FieldInfo field = typeof(EventManager).GetField("GameTickListenersEntity", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((System.Collections.IEnumerable)field.GetValue(manager)!).Cast<object?>().Count(listener => listener != null);
    }

    /// <summary>Sends the player a particle packet naming a provider class that is registered
    /// nowhere, which the engine accepts and a client (or the drain) cannot decode.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="player">The player to send to.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SendUndecodableParticles(ICoreServerAPI api, IServerPlayer player)
        => ((ServerMain)api.World).SendPacket(
            player,
            new Packet_Server
            {
                Id = 61,
                SpawnParticles = new Packet_SpawnParticles { ParticlePropertyProviderClassName = "atlas-no-such-provider", Data = [] },
            });

    /// <summary>Sends, from the player's own connection, the packet a real client sends when it
    /// closes (<c>Leave</c>, packet 14).</summary>
    /// <param name="player">The joined test player.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SendClientLeave(ITestPlayer player)
    {
        var connection = (DummyPlayerConnection)typeof(TestPlayer)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(player)!;
        var leave = new Packet_Client { Id = 14, Leave = new Packet_ClientLeave { Reason = 0 } };
        connection.TcpClient.Send(Packet_ClientSerializer.SerializeToBytes(leave));
    }
}
