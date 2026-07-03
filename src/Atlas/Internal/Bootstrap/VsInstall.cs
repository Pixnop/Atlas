using Atlas.Api;

namespace Atlas.Internal.Bootstrap;

/// <summary>Locates and verifies the Vintage Story installation.</summary>
internal static class VsInstall
{
    /// <summary>Locates the Vintage Story installation directory from the VINTAGE_STORY environment variable.</summary>
    /// <returns>The installation directory path.</returns>
    /// <exception cref="AtlasSetupException">Thrown when VINTAGE_STORY is not set or does not contain VintagestoryLib.dll.</exception>
    public static string Locate()
    {
        string? dir = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "VintagestoryLib.dll")))
        {
            throw new AtlasSetupException(
                "VINTAGE_STORY must point at a Vintage Story install containing VintagestoryLib.dll " +
                $"(current value: '{dir ?? "<unset>"}')");
        }

        return dir;
    }
}
