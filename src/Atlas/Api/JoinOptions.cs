namespace Atlas.Api;

/// <summary>What a test player is joined with, for
/// <see cref="IWorldSession.JoinPlayer(string, JoinOptions)"/>. The defaults are the join
/// <see cref="IWorldSession.JoinPlayer(string)"/> does: the engine's role, and item pickup
/// on.</summary>
public sealed record JoinOptions
{
    /// <summary>Gets the code of the role the player arrives on, or <see langword="null"/> to
    /// keep the engine's own pick for a test player (the highest-privilege role). The code must
    /// be one of the server's configured roles (the engine's <c>serverconfig.json</c>), the ones
    /// <c>IServerPlayer.SetRole</c> accepts, such as <c>"suplayer"</c> for the ordinary survival
    /// role: <see cref="IWorldSession.JoinPlayer(string, JoinOptions)"/> throws an
    /// <see cref="ArgumentException"/> naming the configured ones otherwise.</summary>
    /// <value>The role code. <see langword="null"/> by default.</value>
    /// <remarks>The role is applied by a <c>PlayerJoin</c> handler that
    /// <see cref="IWorldSession.JoinPlayer(string, JoinOptions)"/> subscribes for the length of
    /// the call, so it carries that recipe's limits, written out there: handlers subscribed
    /// before it, a mod's own among them, still see the highest-privilege role, and the role
    /// holds until the engine next reads the player's record.</remarks>
    public string? Role { get; init; }

    /// <summary>Gets a value indicating whether the player picks up the items lying within its
    /// reach, as an ordinary player does. <see langword="false"/> turns the pickup off: a
    /// scenario that drops an item at the player's feet, or lets a mob drop one there, then finds
    /// it on the ground and not in the inventory.</summary>
    /// <value><see langword="true"/> by default.</value>
    /// <remarks>Set through the engine's per-player collect mode, the setting a real client sends
    /// for "collect items only while sneaking". A test player does not sneak unless the scenario
    /// sets <c>Entity.Controls.Sneak</c>, so the pickup stays off until it does. It covers the
    /// pickup of items on the ground, not what a mod gives the player directly
    /// (<c>TryGiveItemStack</c>, <see cref="ITestPlayer.GiveItem"/>).</remarks>
    public bool CollectItems { get; init; } = true;
}
