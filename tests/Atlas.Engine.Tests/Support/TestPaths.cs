namespace Atlas.Engine.Tests.Support;

/// <summary>The paths this suite's fixtures live at, resolved once.</summary>
internal static class TestPaths
{
    /// <summary>This test project's own output directory. Deliberately NOT
    /// <c>AppContext.BaseDirectory</c>: the first host boot in the process redirects that to the
    /// game install (see <c>GameEnvironment.Initialize</c>), and in a full run some earlier test
    /// has almost always booted a host already. The stable reference is this assembly's own
    /// Location, next to which every fixture the suite stages is copied.</summary>
    public static string OwnOutputDirectory { get; } =
        Path.GetDirectoryName(typeof(TestPaths).Assembly.Location)!;

    /// <summary>The deliberately-failing guinea pig assembly, copied next to this one by a
    /// build-only ProjectReference (see Atlas.Engine.Tests.csproj).</summary>
    public static string GuineaPigDll { get; } =
        Path.Combine(OwnOutputDirectory, "Atlas.GuineaPig.Scenarios.dll");
}
