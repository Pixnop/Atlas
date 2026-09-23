namespace Atlas.Pure.Tests.Api;

using Atlas.Api;
using Vintagestory.API.Common;

public class BootDiagnosticEntryTests
{
    [Fact]
    public void DescribeSource_Should_ReturnSourceVerbatim_When_Verified()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Warning, "mymod", "boots unconfigured", null);

        Assert.Equal("mymod", entry.DescribeSource());
    }

    [Fact]
    public void DescribeSource_Should_AppendTheHint_When_SourceIsUnknownAndAHintWasParsed()
    {
        var entry = new BootDiagnosticEntry(
            EnumLogType.Error,
            "unknown",
            "An exception was thrown trying to to load the ModInfo:",
            null,
            SourceHint: "Nimbus.Shared.dll");

        Assert.Equal("unknown, hint Nimbus.Shared.dll", entry.DescribeSource());
    }

    [Fact]
    public void DescribeSource_Should_ReturnUnknownAlone_When_NoHintWasParsed()
    {
        var entry = new BootDiagnosticEntry(
            EnumLogType.Error, "unknown", "Syntax error in json file 'mymod:broken.json'", "mymod:broken.json");

        Assert.Equal("unknown", entry.DescribeSource());
    }

    [Fact]
    public void DescribeSource_Should_IgnoreTheHint_When_SourceIsVerified()
    {
        var entry = new BootDiagnosticEntry(
            EnumLogType.Warning, "mymod", "m", null, SourceHint: "other");

        Assert.Equal("mymod", entry.DescribeSource());
    }
}
