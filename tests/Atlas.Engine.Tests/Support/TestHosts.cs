namespace Atlas.Engine.Tests.Support;

/// <summary>Boots the embedded servers this suite drives directly, outside the xUnit adapter.
/// Every such host needs the same third argument, the directory mod paths and data-file seeds
/// resolve against, and getting it wrong is silent: <c>AppContext.BaseDirectory</c> is the
/// obvious choice and the wrong one, because the first boot in the process redirects it to the
/// game install (see <c>GameEnvironment.Initialize</c>). Tests that captured it before their own
/// boot were right by accident; tests that read it inline were reading the install directory
/// whenever some earlier test had already booted. The base directory is resolved once, from the
/// test assembly's own location (<see cref="TestPaths.OwnOutputDirectory"/>), and nothing else in
/// the suite has to remember the rule.</summary>
/// <remarks>A host that needs data-file seeds or a shortened game-thread join timeout still
/// constructs <c>ServerHost</c> itself, with <see cref="TestPaths.OwnOutputDirectory"/> as that
/// argument; two tests do, and neither is worth an overload.</remarks>
internal static class TestHosts
{
    /// <summary>Boots a host on the default world.</summary>
    /// <param name="mods">Mod dll paths to stage, relative to the suite's output directory.</param>
    /// <returns>The unstarted host.</returns>
    public static ServerHost New(params string[] mods) => New(new WorldOptions(), mods);

    /// <summary>Boots a host on a specific world.</summary>
    /// <param name="options">The world to boot.</param>
    /// <param name="mods">Mod dll paths to stage, relative to the suite's output directory.</param>
    /// <returns>The unstarted host.</returns>
    public static ServerHost New(WorldOptions options, params string[] mods)
        => new(options, mods, TestPaths.OwnOutputDirectory);
}
