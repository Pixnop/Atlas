using System.Reflection;

namespace Atlas.Internal.Bootstrap;

/// <summary>Process-wide, one-time environment fixes so a foreign host can run the game engine.</summary>
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
}
