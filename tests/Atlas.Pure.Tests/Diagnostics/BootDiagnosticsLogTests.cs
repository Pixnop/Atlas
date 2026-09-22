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
        // placeholder has no matching arg (index out of range); this exercises that real throw,
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

    [Theory]
    [InlineData("Server overloaded. A tick took 791ms to complete.")]
    [InlineData("Server overloaded. A tick took 2609ms to complete.")]
    [InlineData("Server overloaded. A tick took 1ms to complete.")]
    public void Add_Should_Discard_When_MessageIsTheServerOverloadedTickWarning(string message)
    {
        // Measured on real CI runs (docs/specs/2026-09-23-boot-diagnostics.md "Environmental
        // noise"): the engine logs this exact shape on a loaded machine, with no mod under test
        // and nothing wrong with any asset. It must never make it into BootDiagnostics, or a slow
        // CI runner fails StrictBootDiagnostics for a reason that has nothing to do with the mod.
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, message, []);

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void Add_Should_Discard_When_ServerOverloadedMessageArrivesAsAFormatStringWithArgs()
    {
        // ILogger.EntryAdded fires the raw format string with args separate (see Format); the
        // filter has to see the shape after formatting, not before.
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "Server overloaded. A tick took {0}ms to complete.", [791]);

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void Add_Should_Keep_When_MessageOnlyResemblesTheServerOverloadedWarning()
    {
        // A mod logging something similar through its own logger is a real entry, not
        // environmental noise: the filter must not over-match on "overloaded" or "tick" alone.
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "[mymod] Server overloaded, retrying the tick.", []);

        BootDiagnosticEntry entry = Assert.Single(log.Snapshot());
        Assert.Equal("mymod", entry.Source);
    }

    [Fact]
    public void Add_Should_LeaveAssetPathNull_When_MessageNamesNoAsset()
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "the boot's background server-assets build did not settle in time", []);

        Assert.Null(Assert.Single(log.Snapshot()).AssetPath);
    }

    [Fact]
    public void Add_Should_IgnoreStackTraceFileLineFragments_When_LookingForAnAssetPath()
    {
        // A raw stack trace line ("in /path/File.cs:line 42") looks like a domain:token to the
        // unanchored pattern ("cs:line") unless a real domain is required not to follow a word
        // character or a dot.
        var log = new BootDiagnosticsLog();
        string message = "Exception: Could not convert string to double: very hard indeed. Path 'resistance'.\n" +
            "   at Vintagestory.Common.JsonHelper.ToDouble() in /src/JsonHelper.cs:line 42";

        log.Add(EnumLogType.Error, message, []);

        Assert.Null(Assert.Single(log.Snapshot()).AssetPath);
    }

    [Fact]
    public void Add_Should_RecordEmptyMessage_When_RawMessageIsNull()
    {
        // LoggerBase.Log's own contract does not rule out a null message (a mod's own
        // api.Logger.Warning(null) is harmless today); recording an empty message instead of
        // throwing keeps that true for whoever ends up subscribed to EntryAdded.
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, null, []);

        Assert.Equal(string.Empty, Assert.Single(log.Snapshot()).Message);
    }

    [Fact]
    public void Add_Should_KeepRawMessage_When_ArgsAreNull()
    {
        var log = new BootDiagnosticsLog();

        log.Add(EnumLogType.Warning, "no substitution needed", null);

        Assert.Equal("no substitution needed", Assert.Single(log.Snapshot()).Message);
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
