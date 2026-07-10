using Atlas.Internal.Staging;

namespace Atlas.Pure.Tests.Staging;

public class SchematicFilesTests
{
    [Fact]
    public void Resolve_Should_KeepPathAsIs_When_PathIsAbsolute()
    {
        string absolute = Path.Combine(Path.GetTempPath(), "fixtures", "structure.json");

        string resolved = SchematicFiles.Resolve(absolute, Path.Combine(Path.GetTempPath(), "elsewhere"));

        Assert.Equal(Path.GetFullPath(absolute), resolved);
    }

    [Fact]
    public void Resolve_Should_ResolveAgainstBaseDir_When_PathIsRelative()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "testbin");

        string resolved = SchematicFiles.Resolve(Path.Combine("fixtures", "structure.json"), baseDir);

        Assert.Equal(Path.Combine(baseDir, "fixtures", "structure.json"), resolved);
    }

    [Fact]
    public void LoadFailureMessage_Should_CarryPathAndEngineError_When_EngineReportedOne()
    {
        string message = SchematicFiles.LoadFailureMessage(
            "/abs/structure.json", "/abs/structure.json", "Failed loading /abs/structure.json : oops");

        Assert.Contains("'/abs/structure.json'", message);
        Assert.Contains("oops", message);
        Assert.DoesNotContain("resolved to", message);
    }

    [Fact]
    public void LoadFailureMessage_Should_NameBothPaths_When_ResolvedPathDiffers()
    {
        string message = SchematicFiles.LoadFailureMessage(
            "structure.json", "/base/structure.json", "it does not exist");

        Assert.Contains("'structure.json'", message);
        Assert.Contains("resolved to '/base/structure.json'", message);
        Assert.Contains("it does not exist", message);
    }

    [Fact]
    public void LoadFailureMessage_Should_ExplainEmptyResult_When_EngineReportedNothing()
    {
        string message = SchematicFiles.LoadFailureMessage(
            "structure.json", "/base/structure.json", string.Empty);

        Assert.Contains("no schematic and no error", message);
    }
}
