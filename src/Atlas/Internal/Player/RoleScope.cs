namespace Atlas.Internal.Player;

/// <summary>The scope behind <see cref="Api.TestPlayerExtensions.WithRole"/>: switches a role on,
/// and puts the role it replaced back when disposed. It works through two delegates instead of a
/// player, so what it does is checkable without a booted server (the
/// <see cref="Hosting.ScratchCleanup"/> pure-core pattern).</summary>
internal sealed class RoleScope : IDisposable
{
    private readonly Action<string> _setRole;
    private readonly string _previous;
    private bool _disposed;

    private RoleScope(Action<string> setRole, string previous)
    {
        _setRole = setRole;
        _previous = previous;
    }

    /// <summary>Reads the current role, switches to <paramref name="role"/>, and returns the scope
    /// that switches back.</summary>
    /// <param name="currentRole">Reads the code of the player's current role.</param>
    /// <param name="setRole">Sets the player's role by code; throws for a role that does not exist,
    /// which then leaves the role as it was.</param>
    /// <param name="role">The code of the role to switch to.</param>
    /// <returns>The scope; disposing it restores the role read here.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="role"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="role"/> is empty.</exception>
    public static RoleScope Enter(Func<string> currentRole, Action<string> setRole, string role)
    {
        ArgumentException.ThrowIfNullOrEmpty(role);

        string previous = currentRole();
        setRole(role);
        return new RoleScope(setRole, previous);
    }

    /// <summary>Puts the role read at <see cref="Enter"/> back. Does nothing the second time.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _setRole(_previous);
    }
}
