using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>Contract of the scratch directory's witness file: the content words the run
/// identifier, the process id, the test assembly and the process start time, the
/// run identifier is shared by the folders of a process and overridable from the environment,
/// and a witness that cannot be written never fails a boot.</summary>
public sealed class ScratchWitnessTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-witness-test-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Describe_Should_NameTheFourFieldsInCamelCase_When_AssemblyAndRunAreKnown()
    {
        string json = ScratchWitness.Describe(
            "abc123", 4242, "My.Scenarios", new DateTime(2026, 10, 4, 14, 30, 5, 250, DateTimeKind.Utc));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("abc123", root.GetProperty("runId").GetString());
        Assert.Equal(4242, root.GetProperty("processId").GetInt32());
        Assert.Equal("My.Scenarios", root.GetProperty("testAssembly").GetString());
        Assert.Equal("2026-10-04T14:30:05.25Z", root.GetProperty("processStartedUtc").GetString());
        Assert.Equal(4, root.EnumerateObject().Count());
    }

    [Fact]
    public void Describe_Should_PutOnePropertyOnEachLine_When_AScriptGreps()
    {
        string json = ScratchWitness.Describe("abc123", 4242, "My.Scenarios", DateTime.UtcNow);

        Assert.Contains("\n  \"processId\": 4242,", json.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Describe_Should_WriteNull_When_NoScenarioClassOwnsTheHost()
    {
        string json = ScratchWitness.Describe("abc123", 4242, null, DateTime.UtcNow);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("testAssembly").ValueKind);
    }

    [Theory]
    [InlineData(null, "generated")]
    [InlineData("", "generated")]
    [InlineData("   ", "generated")]
    [InlineData("ci-42", "ci-42")]
    [InlineData("  ci-42 ", "ci-42")]
    public void ResolveRunId_Should_PreferTheVariable_When_ItIsSetToSomething(string? variable, string expected)
        => Assert.Equal(expected, ScratchWitness.ResolveRunId(variable, "generated"));

    [Fact]
    public void Write_Should_CreateTheFolderAndTheFile_When_TheFolderDoesNotExistYet()
    {
        string folder = Path.Combine(_root.FullName, "atlas", "0123456789abcdef0123456789abcdef");

        ScratchWitness.Write(folder, "My.Scenarios");

        using JsonDocument document = ReadWitness(folder);
        JsonElement root = document.RootElement;
        Assert.Equal(Environment.ProcessId, root.GetProperty("processId").GetInt32());
        Assert.Equal("My.Scenarios", root.GetProperty("testAssembly").GetString());
        Assert.Matches("^[0-9a-f]{32}$", root.GetProperty("runId").GetString());
    }

    [Fact]
    public void Write_Should_RecordTheStartOfTheProcess_When_TheFileIsWritten()
    {
        string folder = Path.Combine(_root.FullName, "start");

        ScratchWitness.Write(folder, "My.Scenarios");

        using JsonDocument document = ReadWitness(folder);
        DateTime recorded = document.RootElement.GetProperty("processStartedUtc").GetDateTime();
        using Process self = Process.GetCurrentProcess();
        Assert.Equal(DateTimeKind.Utc, recorded.Kind);
        Assert.True(
            Math.Abs((recorded - self.StartTime.ToUniversalTime()).TotalSeconds) < 2,
            $"the witness says the process started at {recorded.ToString("O", CultureInfo.InvariantCulture)}");
    }

    [Fact]
    public void Write_Should_GiveTheFoldersOfOneProcessTheSameRunId_When_TwoHostsBoot()
    {
        string first = Path.Combine(_root.FullName, "first");
        string second = Path.Combine(_root.FullName, "second");

        ScratchWitness.Write(first, "My.Scenarios", runIdVariableValue: null);
        ScratchWitness.Write(second, "Other.Scenarios", runIdVariableValue: null);

        using JsonDocument one = ReadWitness(first);
        using JsonDocument two = ReadWitness(second);
        Assert.Equal(
            one.RootElement.GetProperty("runId").GetString(), two.RootElement.GetProperty("runId").GetString());
    }

    [Fact]
    public void Write_Should_RecordTheVariable_When_TheRunIdIsOverridden()
    {
        string folder = Path.Combine(_root.FullName, "override");

        ScratchWitness.Write(folder, "My.Scenarios", runIdVariableValue: "ci-run-42");

        using JsonDocument document = ReadWitness(folder);
        Assert.Equal("ci-run-42", document.RootElement.GetProperty("runId").GetString());
    }

    [Fact]
    public void Write_Should_NotThrow_When_TheFolderCannotBeCreated()
    {
        string blocker = Path.Combine(_root.FullName, "blocker");
        File.WriteAllText(blocker, "a file where a folder is needed");

        Exception? failure = Record.Exception(() => ScratchWitness.Write(Path.Combine(blocker, "atlas"), "My.Scenarios"));

        Assert.Null(failure);
    }

    private static JsonDocument ReadWitness(string folder)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ScratchWitness.FileName)));
}
