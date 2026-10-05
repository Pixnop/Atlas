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
            SourceHint: "MyMod.Shared.dll");

        Assert.Equal("unknown, hint MyMod.Shared.dll", entry.DescribeSource());
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

    [Fact]
    public void Tick_Should_BeNull_When_TheEntryDoesNotSetIt()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Warning, "mymod", "m", null);

        Assert.Null(entry.Tick);
    }

    [Fact]
    public void Tick_Should_LeaveTheConstructorAndDeconstructUnchanged_When_Set()
    {
        // Tick is an init property, not a positional parameter: a consumer that builds an entry
        // or deconstructs one keeps compiling and gets the same five components as before.
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "mymod", "m", "mymod:a", "hint") { Tick = 12 };

        (EnumLogType level, string source, string message, string? assetPath, string? sourceHint) = entry;

        Assert.Equal((EnumLogType.Error, "mymod", "m", "mymod:a", "hint"), (level, source, message, assetPath, sourceHint));
        Assert.Equal(12L, entry.Tick);
    }

    [Fact]
    public void With_Should_KeepTheTick_When_AnotherPropertyChanges()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Warning, "unknown", "m", null, "mymod") { Tick = 7 };

        BootDiagnosticEntry verified = entry with { Source = "mymod", SourceHint = null };

        Assert.Equal(7L, verified.Tick);
    }
}
