using Atlas.Internal.RealClient;

namespace Atlas.Pure.Tests.RealClient;

/// <summary>Contract of the .NET runtime decision for the client's apphost: what the runtime
/// configuration asks for, which folders the apphost looks in, and which of them qualifies.</summary>
public class DotnetRuntimeTests
{
    private const string GameRuntimeConfig = """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
            "configProperties": { "System.Runtime.TieredPGO": true }
          }
        }
        """;

    [Fact]
    public void ReadRequirement_Should_ReturnTheFrameworkAndVersion_When_TheConfigNamesOne()
    {
        RuntimeRequirement? requirement = DotnetRuntime.ReadRequirement(GameRuntimeConfig);

        Assert.Equal(new RuntimeRequirement("Microsoft.NETCore.App", new Version(10, 0, 0)), requirement);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "runtimeOptions": { "includedFrameworks": [] } }""")]
    [InlineData("""{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "ten" } } }""")]
    public void ReadRequirement_Should_ReturnNull_When_TheConfigIsMissingBrokenOrNamesNoFramework(string? json)
        => Assert.Null(DotnetRuntime.ReadRequirement(json));

    [Theory]
    [InlineData("10.0.0", true)]
    [InlineData("10.0.11", true)]
    [InlineData("10.2.0", true)]
    [InlineData("9.0.5", false)]
    [InlineData("11.0.0", false)]
    [InlineData("10.0.0-rc.2.25502.107", false)]
    [InlineData("garbage", false)]
    [InlineData("", false)]
    public void IsSatisfiedBy_Should_RollForwardWithinTheMajor_When_AVersionFolderIsOffered(string installed, bool expected)
        => Assert.Equal(expected, new RuntimeRequirement("Microsoft.NETCore.App", new Version(10, 0, 0)).IsSatisfiedBy(installed));

    [Fact]
    public void IsSatisfiedBy_Should_RejectAnOlderMinor_When_TheRequirementIsHigher()
        => Assert.False(new RuntimeRequirement("Microsoft.NETCore.App", new Version(10, 1, 3)).IsSatisfiedBy("10.0.20"));

    [Fact]
    public void CandidateRoots_Should_TryTheVariableThenTheRegisteredFolderThenTheDefaults()
    {
        IReadOnlyList<string> roots = DotnetRuntime.CandidateRoots("/opt/private-dotnet", "/opt/registered\nignored\n");

        Assert.Equal(["/opt/private-dotnet", "/opt/registered", "/usr/share/dotnet", "/usr/lib/dotnet"], roots);
    }

    [Fact]
    public void CandidateRoots_Should_SkipBlankAndDuplicateEntries_When_TheVariableRepeatsADefault()
    {
        IReadOnlyList<string> roots = DotnetRuntime.CandidateRoots(" /usr/share/dotnet ", "  \n");

        Assert.Equal(["/usr/share/dotnet", "/usr/lib/dotnet"], roots);
    }

    [Fact]
    public void FindRoot_Should_ReturnTheFirstFolderWithAQualifyingRuntime_When_SeveralAreCandidates()
    {
        var requirement = new RuntimeRequirement("Microsoft.NETCore.App", new Version(10, 0, 0));
        var versions = new Dictionary<string, string[]>
        {
            ["/old"] = ["8.0.30", "9.0.2"],
            ["/new"] = ["8.0.30", "10.0.11"],
            ["/newer"] = ["10.0.20"],
        };

        string? root = DotnetRuntime.FindRoot(
            requirement, ["/missing", "/old", "/new", "/newer"], r => versions.GetValueOrDefault(r) ?? []);

        Assert.Equal("/new", root);
    }

    [Fact]
    public void FindRoot_Should_ReturnNull_When_NoCandidateQualifies()
        => Assert.Null(DotnetRuntime.FindRoot(
            new RuntimeRequirement("Microsoft.NETCore.App", new Version(10, 0, 0)), ["/a", "/b"], _ => ["8.0.1"]));
}
