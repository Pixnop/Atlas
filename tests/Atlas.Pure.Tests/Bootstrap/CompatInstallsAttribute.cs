using System.Reflection;
using Xunit.Sdk;

namespace Atlas.Pure.Tests.Bootstrap;

/// <summary>One theory row per Vintage Story install this machine can check the engine contract
/// against: every directory listed in <c>ATLAS_COMPAT_INSTALLS</c> (separated by
/// <see cref="Path.PathSeparator"/>) plus the <c>VINTAGE_STORY</c> install the suite already
/// compiles against, deduplicated by full path. A listed directory holding neither engine
/// assembly still gets a row, which fails; the implicit <c>VINTAGE_STORY</c> one is dropped
/// instead.</summary>
/// <remarks>When neither variable names a usable install the theory is SKIPPED with the reason,
/// not failed: a contributor with a single install, and every CI leg that does not download the
/// compatibility matrix, must still get a green pure suite. xunit 2.9 has no runtime skip
/// (Assert.Skip is a v3 API, and the v2 dynamic-skip token is ignored by this runner), so the
/// skip is declared at discovery time through <see cref="DataAttribute.Skip"/>, which needs one
/// row to attach to; that row is never executed.</remarks>
public sealed class CompatInstallsAttribute : DataAttribute
{
    /// <summary>The environment variable listing the extra installs to check, one per
    /// <see cref="Path.PathSeparator"/>-separated entry.</summary>
    public const string ListVariable = "ATLAS_COMPAT_INSTALLS";

    private readonly IReadOnlyList<string> _installs = Discover();

    /// <summary>Initializes a new instance of the <see cref="CompatInstallsAttribute"/> class,
    /// declaring the skip reason when no install is available.</summary>
    public CompatInstallsAttribute()
    {
        if (_installs.Count == 0)
        {
            Skip = $"No Vintage Story install to check: neither {ListVariable} nor VINTAGE_STORY " +
                   "names a directory holding VintagestoryAPI.dll and VintagestoryLib.dll.";
        }
    }

    /// <inheritdoc/>
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        => _installs.Count == 0 ? [[string.Empty]] : _installs.Select(install => new object[] { install });

    /// <summary>Reports whether a directory holds both engine assemblies, so the theory can fail
    /// a listed entry that does not instead of quietly running one row fewer.</summary>
    /// <param name="dir">The candidate install directory.</param>
    /// <returns><see langword="true"/> when both assemblies are there.</returns>
    internal static bool HoldsEngineAssemblies(string dir)
        => File.Exists(Path.Combine(dir, "VintagestoryAPI.dll"))
           && File.Exists(Path.Combine(dir, "VintagestoryLib.dll"));

    private static IReadOnlyList<string> Discover()
    {
        // A listed entry is an explicit request, so it becomes a row even when it holds no engine
        // assemblies: the row then fails and names the path, rather than leaving a green run with
        // fewer versions checked than whoever set the variable asked for.
        List<string> installs =
        [
            .. (Environment.GetEnvironmentVariable(ListVariable) ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath),
        ];

        // The compile-time install is implicit, so an unusable one is dropped rather than failed,
        // which is what lets the theory skip itself on a machine that has no install at all.
        string compiledAgainst = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? string.Empty;
        if (compiledAgainst.Length != 0 && HoldsEngineAssemblies(Path.GetFullPath(compiledAgainst)))
        {
            installs.Add(Path.GetFullPath(compiledAgainst));
        }

        return [.. installs.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
