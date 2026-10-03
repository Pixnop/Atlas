using System.Runtime.CompilerServices;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Vintagestory.Server.Network;

namespace Atlas.Engine.Tests.Support;

/// <summary>What the client listener and the character gate tests read off the live engine. Like
/// <see cref="EngineProbes"/>, every member is <see cref="MethodImplOptions.NoInlining"/> and only
/// called once a host has booted: a method naming a <c>VintagestoryLib</c> type is resolved when
/// it is compiled, and that assembly only becomes loadable through the host's own late
/// resolver.</summary>
internal static class ListenerProbes
{
    /// <summary>Reads the listener slots and the config.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>The view.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ListenerView View(ICoreServerAPI api)
    {
        var server = (ServerMain)api.World;
        return new ListenerView(
            server.Config.VerifyPlayerAuth,
            server.Config.Password,
            server.MainSockets[ClientListener.Slot]?.GetType().Name,
            server.UdpSockets[ClientListener.Slot]?.GetType().Name,
            server.MainSockets[0]?.GetType().Name);
    }

    /// <summary>Opens the listener on an already running host, over the ports a test chooses.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="pickPort">The candidate ports, one per attempt.</param>
    /// <returns>Where the listener ended up.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ClientEndpoint Open(ICoreServerAPI api, Func<int> pickPort)
        => ClientListener.Open((ServerMain)api.World, pickPort);

    /// <summary>Tells whether a player's connection is one of the in-memory dummy ones.</summary>
    /// <param name="api">The live server API.</param>
    /// <param name="player">A connected player.</param>
    /// <returns><see langword="true"/> for a test player.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsDummyConnection(ICoreServerAPI api, IServerPlayer player)
        => Atlas.Internal.Bootstrap.EngineCompat.IsDummyConnection(((ServerMain)api.World).Clients[player.ClientId]);

    /// <summary>Names the mod-level <c>PlayerJoin</c> handlers in the order the engine raises
    /// them: the type and method each delegate points at.</summary>
    /// <param name="api">The live server API.</param>
    /// <returns>One <c>Type.Method</c> per handler, first to run first.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string[] PlayerJoinHandlers(ICoreServerAPI api)
    {
        ServerEventManager manager = ((ServerMain)api.World).ModEventManager;

        // A field-like event: its backing field is the multicast delegate, in registration order.
        var field = typeof(ServerEventManager).GetField("OnPlayerJoin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var handlers = (Delegate?)field.GetValue(manager);
        return handlers?.GetInvocationList().Select(d => $"{d.Method.DeclaringType!.FullName}.{d.Method.Name}").ToArray() ?? [];
    }

    /// <summary>What the engine holds in the two listener slots and in the config the listener arms.</summary>
    /// <param name="VerifyPlayerAuth">The config's authentication switch.</param>
    /// <param name="Password">The config's server password.</param>
    /// <param name="TcpSlot">The type name in <c>MainSockets[1]</c>, or null when empty.</param>
    /// <param name="UdpSlot">The type name in <c>UdpSockets[1]</c>, or null when empty.</param>
    /// <param name="TcpDummySlot">The type name in <c>MainSockets[0]</c>, or null when empty.</param>
    internal sealed record ListenerView(
        bool VerifyPlayerAuth, string? Password, string? TcpSlot, string? UdpSlot, string? TcpDummySlot);
}
