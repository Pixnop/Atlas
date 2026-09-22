using Atlas.Internal.Diagnostics;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Diagnostics;

public class BootDiagnosticsLogTests
{
    [Theory]
    [InlineData(EnumLogType.Chat)]
    [InlineData(EnumLogType.Event)]
    [InlineData(EnumLogType.StoryEvent)]
    [InlineData(EnumLogType.Build)]
    [InlineData(EnumLogType.VerboseDebug)]
    [InlineData(EnumLogType.Debug)]
    [InlineData(EnumLogType.Notification)]
    [InlineData(EnumLogType.Audit)]
    public void Add_Should_Discard_When_LevelIsBelowWarning(EnumLogType level)
    {
        var log = new BootDiagnosticsLog();

        log.Add(level, "irrelevant", []);

        Assert.Empty(log.Snapshot());
    }

    [Theory]
    [InlineData(EnumLogType.Warning)]
    [InlineData(EnumLogType.Error)]
    [InlineData(EnumLogType.Fatal)]
    public void Add_Should_Keep_When_LevelIsWarningOrAbove(EnumLogType level)
    {
        var log = new BootDiagnosticsLog();

        log.Add(level, "something broke", []);

        BootDiagnosticEntry entry = Assert.Single(log.Snapshot());
        Assert.Equal(level, entry.Level);
        Assert.Equal("something broke", entry.Message);
    }

    [Fact]
    public void Add_Should_FormatWithArgs_When_MessageHasPlaceholders()
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "Failed resolving {0} in {1}", ["game:doesnotexist", "Grid recipe"]);

        Assert.Equal(
            "Failed resolving game:doesnotexist in Grid recipe", Assert.Single(log.Snapshot()).Message);
    }

    [Fact]
    public void Add_Should_KeepRawMessage_When_ArgsDoNotMatchPlaceholders()
    {
        // string.Format silently ignores unused extra args, but throws FormatException when a
        // placeholder has no matching arg (index out of range) - this exercises that real throw,
        // not just an args count that happens not to trigger one.
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "missing arg for {0} and {1}", ["only one"]);

        Assert.Equal("missing arg for {0} and {1}", Assert.Single(log.Snapshot()).Message);
    }

    [Fact]
    public void Add_Should_ReportEngineSource_When_MessageHasNoModPrefix()
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Error, "Syntax error in json file 'mymod:blocktypes/broken.json': boom", []);

        Assert.Equal("engine", Assert.Single(log.Snapshot()).Source);
    }

    [Fact]
    public void Add_Should_ReportModIdAndStripPrefix_When_MessageIsModPrefixed()
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "[mymod] something the mod itself logged", []);

        BootDiagnosticEntry entry = Assert.Single(log.Snapshot());
        Assert.Equal("mymod", entry.Source);
        Assert.Equal("something the mod itself logged", entry.Message);
    }

    [Theory]
    [InlineData(
        "Syntax error in json file 'mymod:blocktypes/broken.json': Failed deserializing broken.json: Unexpected end when reading token. Path ''.",
        "mymod:blocktypes/broken.json")]
    [InlineData(
        "Exception thrown while trying to parse json data of the type with code mymod:badprop, variant mymod:badprop. Will ignore most of the attributes. Exception:",
        "mymod:badprop")]
    [InlineData(
        "Failed resolving crafting recipe ingredient with code game:doesnotexistatall in Grid recipe",
        "game:doesnotexistatall")]
    public void Add_Should_FindAssetPath_When_MessageContainsAnAssetShapedToken(string message, string expected)
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Error, message, []);

        Assert.Equal(expected, Assert.Single(log.Snapshot()).AssetPath);
    }

    [Fact]
    public void Add_Should_LeaveAssetPathNull_When_MessageNamesNoAsset()
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "the boot's background server-assets build did not settle in time", []);

        Assert.Null(Assert.Single(log.Snapshot()).AssetPath);
    }

    [Fact]
    public void Snapshot_Should_PreserveOrder_When_SeveralEntriesAreRecorded()
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "first", []);
        log.Add(EnumLogType.Error, "second", []);
        log.Add(EnumLogType.Fatal, "third", []);

        Assert.Equal(["first", "second", "third"], log.Snapshot().Select(e => e.Message));
    }

    [Fact]
    public void Snapshot_Should_ReturnACopy_When_CalledAfterMoreEntriesAreAdded()
    {
        var log = new BootDiagnosticsLog();
        log.Add(EnumLogType.Warning, "first", []);
        IReadOnlyList<BootDiagnosticEntry> before = log.Snapshot();

        log.Add(EnumLogType.Warning, "second", []);

        Assert.Single(before);
        Assert.Equal(2, log.Snapshot().Count);
    }
}
