using Vintagestory.Client;
using Vintagestory.Server.Network;

namespace Atlas.Internal.Player;

/// <summary>The dummy network endpoints backing one headless test player.</summary>
/// <param name="TcpClient">The dummy client-side TCP connection: used to send further packets.</param>
/// <param name="UdpServer">The dummy UDP server shared by every test player on the embedded
/// server (the engine hard-casts <c>UdpSockets[0]</c> for every singleplayer-type client, so
/// there can only be one).</param>
/// <param name="TcpSlot">The index this player's TCP socket occupies in the server's
/// <c>MainSockets</c> array, so a failed join can release exactly that slot.</param>
internal readonly record struct DummyPlayerConnection(DummyTcpNetClient TcpClient, DummyUdpNetServer UdpServer, int TcpSlot);
