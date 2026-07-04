using Vintagestory.Client;
using Vintagestory.Server.Network;

namespace Atlas.Internal.Player;

/// <summary>The dummy network endpoints backing one headless test player.</summary>
/// <param name="TcpClient">The dummy client-side TCP connection: used to send further packets.</param>
/// <param name="UdpServer">The dummy UDP server claimed on the embedded server.</param>
internal readonly record struct DummyPlayerConnection(DummyTcpNetClient TcpClient, DummyUdpNetServer UdpServer);
