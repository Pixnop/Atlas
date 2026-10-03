namespace Atlas.Internal.Hosting;

/// <summary>Where a real game client can reach an embedded host: the loopback address, the port
/// both listeners share, and the random password the engine asks the client for.</summary>
/// <param name="Host">The bound address, always <see cref="ClientListener.Loopback"/>.</param>
/// <param name="Port">The port the TCP and the UDP listener are both bound to.</param>
/// <param name="Password">The random server password, to pass as <c>--pw</c>.</param>
internal sealed record ClientEndpoint(string Host, int Port, string Password)
{
    /// <summary>Gets the <c>host:port</c> form <c>--connect</c> takes.</summary>
    public string Address => $"{Host}:{Port}";
}
