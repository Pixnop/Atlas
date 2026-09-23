using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class AttributeMappingTests : IDisposable
{
    private static readonly string ManifestPath = Path.Combine(
        Path.GetDirectoryName(typeof(AttributeMappingTests).Assembly.Location)!,
        AttributeMapper.ManifestFileName);

    private static readonly string[] AssemblyModOnly = ["assembly-mod.dll"];
    private static readonly string[] AssemblyThenClassMods = ["assembly-mod.dll", "class-mod.dll"];
    private static readonly string[] FakeModManifest = ["C:\\mods\\FakeMod.dll"];
    private static readonly string[] ManifestWithBlankLines =
        ["C:\\mods\\FakeMod.dll", string.Empty, "   ", "C:\\mods\\OtherMod.dll"];

    private static readonly string[] AssemblyThenFakeMod =
        ["assembly-mod.dll", "C:\\mods\\FakeMod.dll"];

    private static readonly string[] AssemblyThenBothManifestMods =
        ["assembly-mod.dll", "C:\\mods\\FakeMod.dll", "C:\\mods\\OtherMod.dll"];

    private static readonly string[] AssemblyClassThenManifestMods =
        ["assembly-mod.dll", "class-mod.dll", "C:\\mods\\FakeMod.dll"];

    public void Dispose()
    {
        if (File.Exists(ManifestPath))
        {
            File.Delete(ManifestPath);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Map_Should_ThrowArgumentNullException_When_TestClassIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AttributeMapper.Map(null!));
    }

    [Fact]
    public void Map_Should_UseDefaults_When_ClassHasNoAtlasWorldAttribute()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Equal("424242", recipe.Options.Seed);
        Assert.Equal("superflat", recipe.Options.WorldType);
        Assert.Equal("creativebuilding", recipe.Options.PlayStyle);
    }

    [Fact]
    public void Map_Should_ConvertSeedToString_When_AtlasWorldSpecifiesSeed()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(SeededScenario));

        Assert.Equal("7", recipe.Options.Seed);
    }

    [Fact]
    public void Map_Should_UseWorldTypeAndPlayStyle_When_AtlasWorldOverridesThem()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(CustomWorldScenario));

        Assert.Equal("flat", recipe.Options.WorldType);
        Assert.Equal("surviveandbuild", recipe.Options.PlayStyle);
    }

    [Fact]
    public void Map_Should_ConcatenateAssemblyThenClassMods_When_BothArePresent()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(ClassModsScenario));

        Assert.Equal(AssemblyThenClassMods, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_UseOnlyAssemblyMods_When_ClassHasNoExtraMods()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Equal(AssemblyModOnly, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_UseAssemblyLocationDirectory_When_ResolvingModBaseDir()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        string expected = Path.GetDirectoryName(typeof(NoAttributeScenario).Assembly.Location)!;
        Assert.Equal(expected, recipe.ModBaseDir);
    }

    [Fact]
    public void Map_Should_AppendManifestPaths_When_GeneratedManifestFileExists()
    {
        File.WriteAllLines(ManifestPath, FakeModManifest);

        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Equal(AssemblyThenFakeMod, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_IgnoreBlankLines_When_GeneratedManifestFileHasBlankLines()
    {
        File.WriteAllLines(ManifestPath, ManifestWithBlankLines);

        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Equal(AssemblyThenBothManifestMods, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_NotAppendAnything_When_GeneratedManifestFileIsAbsent()
    {
        Assert.False(File.Exists(ManifestPath));

        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Equal(AssemblyModOnly, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_LeaveSaveFileUnset_When_ClassHasNoAtlasWorldAttribute()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Null(recipe.Options.SaveFile);
    }

    [Fact]
    public void Map_Should_UseSaveFile_When_AtlasWorldDeclaresOne()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(SaveFileScenario));

        Assert.Equal("fixtures/prebuilt-world.vcdbs", recipe.Options.SaveFile);
    }

    [Fact]
    public void Map_Should_EnableStrictBootDiagnostics_When_AtlasWorldDeclaresIt()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(StrictBootScenario));

        Assert.True(recipe.Options.StrictBootDiagnostics);
    }

    [Fact]
    public void Map_Should_LeaveStrictBootDiagnosticsOff_When_ClassHasNoAtlasWorldAttribute()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.False(recipe.Options.StrictBootDiagnostics);
    }

    [Fact]
    public void Map_Should_UseOnlyAssemblyDataFiles_When_ClassHasNoAtlasDataFilesAttribute()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        DataFileSeed seed = Assert.Single(recipe.DataFiles);
        Assert.Equal(new DataFileSeed("assembly-data", "ModConfig"), seed);
    }

    [Fact]
    public void Map_Should_OrderAssemblySeedsBeforeClassSeeds_When_BothDeclareDataFiles()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(DataFilesScenario));

        Assert.Equal(
            new[]
            {
                new DataFileSeed("assembly-data", "ModConfig"),
                new DataFileSeed("class-data-a", string.Empty),
                new DataFileSeed("class-data-b", string.Empty),
            },
            recipe.DataFiles);
    }

    [Fact]
    public void Map_Should_ApplyTargetPathToEverySourcePath_When_AttributeDeclaresSeveral()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(TargetedDataFilesScenario));

        Assert.Contains(new DataFileSeed("class-data-a", "ModConfig"), recipe.DataFiles);
        Assert.Contains(new DataFileSeed("class-data-b", "ModConfig"), recipe.DataFiles);
    }

    [Fact]
    public void Map_Should_OrderAttributePathsBeforeManifestPaths_When_ClassAndManifestBothContributeMods()
    {
        File.WriteAllLines(ManifestPath, FakeModManifest);

        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(ClassModsScenario));

        Assert.Equal(AssemblyClassThenManifestMods, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_ExcludeAssemblyModsAndManifest_When_ClassOptsOut()
    {
        File.WriteAllLines(ManifestPath, FakeModManifest);

        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(VanillaScenario));

        Assert.Empty(recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_KeepOnlyItsOwnMods_When_ClassOptsOutAndDeclaresMods()
    {
        File.WriteAllLines(ManifestPath, FakeModManifest);

        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(VanillaWithOwnModsScenario));

        Assert.Equal(new[] { "class-mod.dll" }, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_IncludeAssemblyMods_When_ClassDoesNotOptOut()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(ClassModsScenario));

        Assert.Equal(AssemblyThenClassMods, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_LeaveAllowedBootDiagnosticsEmpty_When_ClassHasNoAtlasAllowBootDiagnosticAttribute()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        AllowedBootDiagnostic allowed = Assert.Single(recipe.Options.AllowedBootDiagnostics);
        Assert.Equal("assembly-level pattern", allowed.MessagePattern);
    }

    [Fact]
    public void Map_Should_CombineAssemblyAndClassAllowedBootDiagnostics_When_BothDeclareOne()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(AllowedDiagnosticsScenario));

        Assert.Equal(
            new[] { "assembly-level pattern", "class-level pattern" },
            recipe.Options.AllowedBootDiagnostics.Select(a => a.MessagePattern));
    }

    [Fact]
    public void Map_Should_CarryLevelAndSource_When_AtlasAllowBootDiagnosticDeclaresThem()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(AllowedDiagnosticsScenario));

        AllowedBootDiagnostic classRule = recipe.Options.AllowedBootDiagnostics.Last();
        Assert.Equal("Warning", classRule.Level);
        Assert.Equal("mymod", classRule.Source);
    }

    [Fact]
    public void Map_Should_NameTheDeclaringClassOrAssembly_When_CollectingAllowedBootDiagnostics()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(AllowedDiagnosticsScenario));

        AllowedBootDiagnostic assemblyRule = recipe.Options.AllowedBootDiagnostics.First();
        AllowedBootDiagnostic classRule = recipe.Options.AllowedBootDiagnostics.Last();
        Assert.StartsWith("assembly '", assemblyRule.DeclaredOn, StringComparison.Ordinal);
        Assert.Equal($"class '{typeof(AllowedDiagnosticsScenario).FullName}'", classRule.DeclaredOn);
    }

    private class NoAttributeScenario
    {
    }

    [AtlasWorld(Seed = 7)]
    private class SeededScenario
    {
    }

    [AtlasWorld(WorldType = "flat", PlayStyle = "surviveandbuild")]
    private class CustomWorldScenario
    {
    }

    [AtlasWorld(Mods = new[] { "class-mod.dll" })]
    private class ClassModsScenario
    {
    }

    [AtlasWorld(SaveFile = "fixtures/prebuilt-world.vcdbs")]
    private class SaveFileScenario
    {
    }

    [AtlasWorld(StrictBootDiagnostics = true)]
    private class StrictBootScenario
    {
    }

    [AtlasDataFiles("class-data-a", "class-data-b")]
    private class DataFilesScenario
    {
    }

    [AtlasDataFiles("class-data-a", "class-data-b", TargetPath = "ModConfig")]
    private class TargetedDataFilesScenario
    {
    }

    [AtlasWorld(ExcludeAssemblyMods = true)]
    private class VanillaScenario
    {
    }

    [AtlasWorld(ExcludeAssemblyMods = true, Mods = new[] { "class-mod.dll" })]
    private class VanillaWithOwnModsScenario
    {
    }

    [AtlasAllowBootDiagnostic("class-level pattern", Level = "Warning", Source = "mymod")]
    private class AllowedDiagnosticsScenario
    {
    }
}
