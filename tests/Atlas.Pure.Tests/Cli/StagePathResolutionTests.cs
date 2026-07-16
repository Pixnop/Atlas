using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class StagePathResolutionTests
{
    [Theory]
    [InlineData("out/Scenarios.dll", "out")]
    [InlineData("out/Scenarios.DLL", "out")]
    [InlineData("/abs/path/out/Scenarios.dll", "/abs/path/out")]
    public void ResolveTargetDirectory_Should_ReturnTheContainingDirectory_When_TargetIsADll(
        string targetPath, string expectedDirectory)
    {
        string resolved = StagePathResolution.ResolveTargetDirectory(targetPath);

        Assert.Equal(expectedDirectory, resolved);
    }

    [Theory]
    [InlineData("out/")]
    [InlineData("out")]
    [InlineData("/abs/path/out")]
    [InlineData("/abs/path/out/")]
    public void ResolveTargetDirectory_Should_ReturnItUnchanged_When_TargetIsNotADllPath(string targetPath)
    {
        string resolved = StagePathResolution.ResolveTargetDirectory(targetPath);

        Assert.Equal(targetPath.TrimEnd('/'), resolved);
    }

    [Fact]
    public void ResolveTargetDirectory_Should_ReturnCurrentDirectoryMarker_When_DllHasNoContainingDirectory()
    {
        string resolved = StagePathResolution.ResolveTargetDirectory("Scenarios.dll");

        Assert.Equal(".", resolved);
    }

    [Fact]
    public void ResolveTargetDirectory_Should_ReturnTheOriginal_When_TargetIsAllSeparators()
    {
        string resolved = StagePathResolution.ResolveTargetDirectory("///");

        Assert.Equal("///", resolved);
    }
}
