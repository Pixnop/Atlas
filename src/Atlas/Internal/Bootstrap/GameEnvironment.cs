using System.Reflection;
using System.Runtime.CompilerServices;

namespace Atlas.Internal.Bootstrap;

/// <summary>Process-wide, one-time environment fixes so a foreign host can run the game engine.</summary>
/// <remarks><see cref="Initialize"/> redirects <see cref="AppContext.BaseDirectory"/> (via the
/// "APP_CONTEXT_BASE_DIRECTORY" AppDomain data slot) to the Vintage Story install directory, since
/// <c>GamePaths.Binaries</c> and the mod compiler resolve against it. Callers must therefore capture
/// <see cref="AppContext.BaseDirectory"/> (e.g. to compute a test project's own output directory)
/// BEFORE the first server boot in the process; reading it afterward returns the install directory,
/// not the original one. This redirect - and everything else <see cref="Initialize"/> does - is only
/// relevant to a process that actually boots a server, so it is only ever called explicitly from
/// <see cref="Internal.Hosting.ServerHost"/>'s game thread. The assembly resolve hook itself is
/// registered separately and much earlier, by <see cref="TryRegisterResolveHookEarly"/>; see its own
/// remarks for why that split exists.</remarks>
internal static class GameEnvironment
{
    private static int _initialized;

    /// <summary>Applies the process-wide fixes needed to embed the game engine, exactly once per process.</summary>
    /// <param name="installDir">The Vintage Story installation directory.</param>
    /// <remarks>Does NOT register the <see cref="AppDomain.AssemblyResolve"/> hook: that is owned by
    /// the module initializer (<see cref="TryRegisterResolveHookEarly"/>), which runs unconditionally
    /// at assembly load, before this method is ever reached. Registering it again here would just add
    /// a second, redundant handler.</remarks>
    public static void Initialize(string installDir)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", installDir + Path.DirectorySeparatorChar);

        // The engine's mod loader scans mod dlls with Mono.Cecil's DEFAULT assembly resolver,
        // whose search path is the process current directory - the install directory when the
        // real game runs. If the resolver cannot find VintagestoryAPI there, every base-game
        // mod fails its ModInfo scan, zero mod systems load, and the boot dies in
        // selectPlayStyle (playstyles are defined by the survival mod). Test runs have mostly
        // been saved by the test bin happening to hold a VintagestoryAPI.dll copy, which is
        // luck, not design (issue #32; trap found by the client-testing spike). Atlas resolves
        // consumer mod paths against the test assembly's location, never the current
        // directory, so nothing else observes this change.
        Directory.SetCurrentDirectory(installDir);
    }

    /// <summary>Registers the <see cref="AppDomain.AssemblyResolve"/> hook eagerly, as a module
    /// initializer, so it is in place before any caller's method that references VintagestoryAPI
    /// types is JITted.</summary>
    /// <remarks>The CLR resolves a method's whole signature - including lambda parameter types - on
    /// the calling thread, the first time that method is invoked. Without an eager hook, that
    /// resolution can happen strictly before <see cref="Internal.Hosting.ServerHost"/> ever reaches
    /// the game thread where <see cref="Initialize"/> is called, so the very first reference to
    /// <c>ICoreServerAPI</c> et al. in a consumer's code would fail with a
    /// <see cref="System.IO.FileNotFoundException"/> for VintagestoryAPI.
    /// <para>This is deliberately the ONLY thing that runs eagerly. Unlike <see cref="Initialize"/>,
    /// registering the hook has no observable effect on a process that never asks the CLR to resolve
    /// a game assembly - which includes every pure unit test process - because the hook is a callback
    /// that only executes when <see cref="AppDomain.AssemblyResolve"/> actually fires. It also never
    /// reads <c>VINTAGE_STORY</c> or touches the filesystem at module-init time: the install directory
    /// is computed lazily, inside the callback itself, the first time (if ever) it is invoked. So a
    /// process with no Vintage Story install configured, or an invalid one, is completely unaffected -
    /// the hook is registered, then simply never fires and never throws.</para></remarks>
#pragma warning disable CA2255 // Module initializer is the deliberate mechanism here: it closes an
    // ordering gap between AssemblyResolve registration and the CLR resolving VintagestoryAPI while
    // JITting the first caller method that references engine types (see remarks above).
    [ModuleInitializer]
    internal static void TryRegisterResolveHookEarly()
#pragma warning restore CA2255
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            string? installDir = Environment.GetEnvironmentVariable("VINTAGE_STORY");
            if (string.IsNullOrEmpty(installDir))
            {
                return null;
            }

            string file = new AssemblyName(args.Name).Name + ".dll";
            foreach (string dir in new[] { installDir, Path.Combine(installDir, "Lib"), Path.Combine(installDir, "Mods") })
            {
                string candidate = Path.Combine(dir, file);
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }

            return null;
        };
    }
}
