using Atlas.Api;
using Atlas.Internal.Diagnostics;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Diagnostics;

public class DependencyModHintTests
{
    private const string EngineMessage =
        "Exception: /tmp/atlas/some-guid/TestMods/Shared.dll declared as code mod, but there are " +
        "no .dll files that contain at least one ModSystem or has a ModInfo attribute\n   at " +
        "Vintagestory.Common.ModContainer.LoadModInfoFromCode(...)";

    [Fact]
    public void Describe_Should_ReturnTheHint_When_TheEntryIsTheEngineMessage_And_ItsHintedFileWasStaged()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "unknown", EngineMessage, null, SourceHint: "Shared.dll");

        string? hint = DependencyModHint.Describe(entry, ["mod/MyMod.dll", "mod/Shared.dll"]);

        Assert.NotNull(hint);
        Assert.Contains("dependency dll staged as a mod", hint, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Pixnop/Atlas/wiki/Mod-Staging", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_ReturnNull_When_TheHintedFileWasNotOneOfTheStagedModPaths()
    {
        // The failing dll is not one Atlas itself staged (a bare-name coincidence, or a stale
        // hint left from before some other mod path change): guessing would be unfounded.
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "unknown", EngineMessage, null, SourceHint: "Shared.dll");

        string? hint = DependencyModHint.Describe(entry, ["mod/MyMod.dll"]);

        Assert.Null(hint);
    }

    [Fact]
    public void Describe_Should_ReturnNull_When_SourceIsAlreadyVerified()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "mymod", EngineMessage, null);

        string? hint = DependencyModHint.Describe(entry, ["mod/Shared.dll"]);

        Assert.Null(hint);
    }

    [Fact]
    public void Describe_Should_ReturnNull_When_NoSourceHintWasParsed()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "unknown", EngineMessage, null);

        string? hint = DependencyModHint.Describe(entry, ["mod/Shared.dll"]);

        Assert.Null(hint);
    }

    [Fact]
    public void Describe_Should_ReturnNull_When_TheMessageIsNotTheEngineDeclaredAsCodeModFailure()
    {
        // The other entry the same failure always logs alongside the real one: same source hint,
        // but no engine message to recognize, so it must not also get the hint.
        var entry = new BootDiagnosticEntry(
            EnumLogType.Error,
            "unknown",
            "An exception was thrown trying to to load the ModInfo:",
            null,
            SourceHint: "Shared.dll");

        string? hint = DependencyModHint.Describe(entry, ["mod/Shared.dll"]);

        Assert.Null(hint);
    }

    [Fact]
    public void Describe_Should_MatchByFileNameOnly_When_TheAtlasModsPathIsNested()
    {
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "unknown", EngineMessage, null, SourceHint: "Shared.dll");

        string? hint = DependencyModHint.Describe(entry, ["deps/nested/Shared.dll"]);

        Assert.NotNull(hint);
    }

    [Fact]
    public void Describe_Should_ReturnNull_When_TheMatchingAtlasModsPathIsAFolderNotADll()
    {
        // A folder (or zip) mod whose own modinfo.json declares a code mod with no ModSystem dll
        // hits the same engine message: SourceHint is then the folder's modid, and a folder
        // listed in AtlasMods named after its own modid (a common convention) would otherwise
        // match. That is a real mod, not a dependency staged as one; the hint must not fire.
        var entry = new BootDiagnosticEntry(EnumLogType.Error, "unknown", EngineMessage, null, SourceHint: "Shared");

        string? hint = DependencyModHint.Describe(entry, ["mod/Shared"]);

        Assert.Null(hint);
    }
}
