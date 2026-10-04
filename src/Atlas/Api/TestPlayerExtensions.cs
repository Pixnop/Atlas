using Atlas.Internal.Player;

namespace Atlas.Api;

/// <summary>Helpers over <see cref="ITestPlayer"/> that are not part of its interface, so a
/// consumer that implements the interface does not have to carry them.</summary>
public static class TestPlayerExtensions
{
    /// <summary>Puts the player on a role until the returned scope is disposed, then puts back
    /// the role it held when this was called: a scenario that wants one refusal, or one command
    /// as a restricted player, without leaving the player demoted for whoever uses it next.</summary>
    /// <param name="player">The joined test player.</param>
    /// <param name="role">The code of the role to switch to, one of the server's configured
    /// roles (<c>"suplayer"</c> is the ordinary survival role, <c>"admin"</c> the highest).</param>
    /// <returns>The scope. Disposing it a second time does nothing. Nested scopes end in the
    /// order they were opened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="player"/> or
    /// <paramref name="role"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="role"/> is empty or is not
    /// a configured role; the player's role is left as it was.</exception>
    /// <remarks><para>Runs on the game thread. It is <c>player.Player.SetRole(role)</c> with the
    /// way back kept for you, so it has the limit of <c>SetRole</c>: the role lasts only until the
    /// engine next reads the player's record, which puts a test player back on the highest role
    /// (a mod granting it a privilege, or one of the commands listed in
    /// <see cref="ITestPlayer.ExecuteCommand"/>). Disposing the scope restores the role it found
    /// whatever the player holds by then, so the player ends on that role either way. To have a
    /// player on a role from its join on, set <see cref="JoinOptions.Role"/> instead.</para></remarks>
    public static IDisposable WithRole(this ITestPlayer player, string role)
    {
        ArgumentNullException.ThrowIfNull(player);
        return RoleScope.Enter(() => player.Player.Role.Code, code => player.Player.SetRole(code), role);
    }
}
