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

    private static BootDiagnosticEntry Entry(string message) => new(EnumLogType.Warning, "mymod", message, null);
}
