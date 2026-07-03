using System.Reflection;
using System.Runtime.CompilerServices;
using Atlas.Api;

namespace Atlas.Internal.Bootstrap;

/// <summary>Process-wide, one-time environment fixes so a foreign host can run the game engine.</summary>
/// <remarks><see cref="Initialize"/> redirects <see cref="AppContext.BaseDirectory"/> (via the
/// "APP_CONTEXT_BASE_DIRECTORY" AppDomain data slot) to the Vintage Story install directory, since
/// <c>GamePaths.Binaries</c> and the mod compiler resolve against it. Callers must therefore capture
/// <see cref="AppContext.BaseDirectory"/> (e.g. to compute a test project's own output directory)
/// BEFORE the first server boot in the process; reading it afterward returns the install directory,
/// not the original one.
/// <para><see cref="TryInitializeEarly"/> runs as a module initializer so the
/// <see cref="AppDomain.AssemblyResolve"/> hook is registered before any caller's method that
/// references VintagestoryAPI types is JITted. The CLR resolves such a method's whole signature
/// (including lambda parameter types) on first invocation, on the calling thread; without an
/// eager hook, that resolution happens strictly before <see cref="Internal.Hosting.ServerHost"/>
/// ever reaches the game thread where <see cref="Initialize"/> was previously the only call site,
/// so the very first reference to <c>ICoreServerAPI</c> et al. in a consumer's code would fail
/// with a <see cref="System.IO.FileNotFoundException"/> for VintagestoryAPI.</para></remarks>
#pragma warning disable CA2255 // Module initializer is the deliberate mechanism here: it closes an
// ordering gap between AssemblyResolve registration and the CLR resolving VintagestoryAPI while
// JITting the first caller method that references engine types (see remarks above).
internal static class GameEnvironment
{
    private static int _initialized;

    /// <summary>Applies the process-wide fixes needed to embed the game engine, exactly once per process.</summary>
    /// <param name="installDir">The Vintage Story installation directory.</param>
    public static void Initialize(string installDir)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", installDir + Path.DirectorySeparatorChar);
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
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

    /// <summary>Best-effort eager initialization at assembly load, so the assembly resolve hook
    /// is in place before any caller's code referencing VintagestoryAPI types gets JITted.
    /// Swallows failures (e.g. VINTAGE_STORY unset): callers without a real install never touch
    /// engine types, and <see cref="Internal.Hosting.ServerHost"/> re-attempts setup on the game
    /// thread, surfacing a proper <see cref="Atlas.Api.AtlasSetupException"/> there instead.</summary>
    [ModuleInitializer]
    internal static void TryInitializeEarly()
    {
        try
        {
            Initialize(VsInstall.Locate());
        }
        catch (AtlasSetupException)
        {
            // No valid install yet; ServerHost.GameThreadMain will retry and surface the error.
        }
    }
}
#pragma warning restore CA2255
