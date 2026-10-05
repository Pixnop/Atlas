using Atlas.Api;
using Atlas.Internal.Hosting;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>The message a strict boot fails with: its count agrees with its verb, and it names
/// the boot's kept scratch folder and server log, so the failure is traceable without the
/// scenario's output.</summary>
public class StrictFailureMessageTests
{
    private const string DataPath = "/scratch/atlas/deadbeef";

    [Fact]
    public void DescribeStrictFailure_Should_UseTheSingular_When_OneEntryIsOffending()
    {
        string message = ServerHost.DescribeStrictFailure([Entry("first")], [], DataPath);

        Assert.StartsWith("Boot diagnostics: 1 entry at Warning level or above was logged ", message, StringComparison.Ordinal);
        Assert.DoesNotContain("were logged", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeStrictFailure_Should_UseThePlural_When_SeveralEntriesAreOffending()
    {
        string message = ServerHost.DescribeStrictFailure([Entry("first"), Entry("second")], [], DataPath);

        Assert.StartsWith("Boot diagnostics: 2 entries at Warning level or above were logged ", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeStrictFailure_Should_NameTheKeptScratchFolderAndTheServerLog_When_Formatted()
    {
        string message = ServerHost.DescribeStrictFailure([Entry("first")], [], DataPath);

        Assert.Contains($"scratch folder is kept: {DataPath}", message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(DataPath, "Logs", "server-main.log"), message, StringComparison.Ordinal);
        Assert.Contains("  - Warning [mymod] first", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeStrictFailure_Should_ListTheUnmetRule_When_NoEntryIsOffending()
    {
        string[] unmet = ["[AtlasAllowBootDiagnostic(\"gone\", Required = true)] declared on class 'X', matched no boot entry, expected at least one"];

        string message = ServerHost.DescribeStrictFailure([], [], DataPath, unmet);

        Assert.StartsWith(
            "Boot diagnostics: 1 [AtlasAllowBootDiagnostic] rule did not get the entries it requires " +
            "(strict mode, [AtlasWorld(StrictBootDiagnostics = true)]):\n  - [AtlasAllowBootDiagnostic(\"gone\"",
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("were logged", message, StringComparison.Ordinal);
        Assert.DoesNotContain("was logged", message, StringComparison.Ordinal);
        Assert.Contains($"scratch folder is kept: {DataPath}", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeStrictFailure_Should_UseThePluralForRules_When_SeveralAreUnmet()
    {
        string message = ServerHost.DescribeStrictFailure([], [], DataPath, ["first", "second"]);

        Assert.StartsWith(
            "Boot diagnostics: 2 [AtlasAllowBootDiagnostic] rules did not get the entries they require ",
            message,
            StringComparison.Ordinal);
        Assert.Contains("\n  - first\n  - second\n", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeStrictFailure_Should_ListTheOffendingEntriesBeforeTheUnmetRules_When_BothExist()
    {
        string message = ServerHost.DescribeStrictFailure([Entry("first")], [], DataPath, ["a rule"]);

        int entries = message.IndexOf("  - Warning [mymod] first", StringComparison.Ordinal);
        int rules = message.IndexOf("  - a rule", StringComparison.Ordinal);
        Assert.True(entries >= 0 && rules > entries);
        Assert.Equal(1, message.Split("scratch folder is kept").Length - 1);
    }

    private static BootDiagnosticEntry Entry(string message) => new(EnumLogType.Warning, "mymod", message, null);
}
