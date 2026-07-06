using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class ScenarioAssemblyResolverTests
{
    [Fact]
    public void ResolvePath_Should_MapSimpleNameToDll_When_FileExists()
    {
        string? path = ScenarioAssemblyResolver.ResolvePath("Atlas.XUnit", "/bin", _ => true);

        Assert.Equal(Path.Combine("/bin", "Atlas.XUnit.dll"), path);
    }

    [Fact]
    public void ResolvePath_Should_StripVersionAndCulture_When_FullNameIsGiven()
    {
        string? path = ScenarioAssemblyResolver.ResolvePath(
            "xunit.execution.dotnet, Version=2.9.3.0, Culture=neutral, PublicKeyToken=8d05b1bb7a6fdb6c",
            "/bin",
            _ => true);

        Assert.Equal(Path.Combine("/bin", "xunit.execution.dotnet.dll"), path);
    }

    [Fact]
    public void ResolvePath_Should_ReturnNull_When_FileIsAbsent()
    {
        Assert.Null(ScenarioAssemblyResolver.ResolvePath("Missing", "/bin", _ => false));
    }
}
