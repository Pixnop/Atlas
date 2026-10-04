using System.Reflection;
using System.Reflection.Emit;
using Atlas.Api;
using Atlas.Internal.Bootstrap;
using Atlas.Internal.Diagnostics;
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

    private static readonly string[] ClassModOnly = ["class-mod.dll"];
    private static readonly string[] AssemblyThenClassBootDiagnosticPatterns =
        ["assembly-level pattern", "class-level pattern"];

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

        Assert.Equal(ClassModOnly, recipe.ModPaths);
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
            AssemblyThenClassBootDiagnosticPatterns,
            recipe.Options.AllowedBootDiagnostics.Select(a => a.MessagePattern));
    }

    [Fact]
    public void Map_Should_CarryLevelAndSource_When_AtlasAllowBootDiagnosticDeclaresThem()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(AllowedDiagnosticsScenario));

        AllowedBootDiagnostic classRule = recipe.Options.AllowedBootDiagnostics[^1];
        Assert.Equal("Warning", classRule.Level);
        Assert.Equal("mymod", classRule.Source);
    }

    [Fact]
    public void Map_Should_NameTheDeclaringClassOrAssembly_When_CollectingAllowedBootDiagnostics()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(AllowedDiagnosticsScenario));

        AllowedBootDiagnostic assemblyRule = recipe.Options.AllowedBootDiagnostics[0];
        AllowedBootDiagnostic classRule = recipe.Options.AllowedBootDiagnostics[^1];
        Assert.StartsWith("assembly '", assemblyRule.DeclaredOn, StringComparison.Ordinal);
        Assert.Equal($"class '{typeof(AllowedDiagnosticsScenario).FullName}'", classRule.DeclaredOn);
    }

    [Fact]
    public void Map_Should_CarryRequiredAndCount_When_AtlasAllowBootDiagnosticDeclaresThem()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(RequiredAndCountScenario));

        AllowedBootDiagnostic required = recipe.Options.AllowedBootDiagnostics.Single(a => a.MessagePattern == "required one");
        AllowedBootDiagnostic counted = recipe.Options.AllowedBootDiagnostics.Single(a => a.MessagePattern == "counted two");
        Assert.True(required.Required);
        Assert.Null(required.Count);
        Assert.False(counted.Required);
        Assert.Equal(2, counted.Count);
    }

    [Fact]
    public void Map_Should_LeaveRequiredAndCountUnset_When_TheAttributeDeclaresNeither()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(AllowedDiagnosticsScenario));

        AllowedBootDiagnostic classRule = recipe.Options.AllowedBootDiagnostics[^1];
        Assert.False(classRule.Required);
        Assert.Null(classRule.Count);
    }

    [Fact]
    public void Map_Should_KeepAssemblyLevelRequiredAndCount_When_TheClassLoadsTheAssemblyMods()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        AllowedBootDiagnostic rule = Assert.Single(recipe.Options.AllowedBootDiagnostics);
        Assert.True(rule.Required);
        Assert.Equal(2, rule.Count);
    }

    [Fact]
    public void Map_Should_StillAllowButNotRequire_When_AnAssemblyLevelRuleMeetsAClassThatExcludesTheAssemblyMods()
    {
        // The mod that logs the entry is one of the assembly's mods, which this class does not
        // load: the entry cannot appear, so the rule cannot be unmet. It keeps allowing what it
        // matches, since a class never loses an assembly-wide allowance.
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(VanillaScenario));

        AllowedBootDiagnostic rule = Assert.Single(recipe.Options.AllowedBootDiagnostics);
        Assert.Equal("assembly-level pattern", rule.MessagePattern);
        Assert.False(rule.Required);
        Assert.Null(rule.Count);
    }

    [Fact]
    public void Unmet_Should_NameTheAssemblyLevelRule_When_TheClassLoadsTheAssemblyModsAndTheBootLoggedNothing()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        string unmet = Assert.Single(BootDiagnosticsAllowlist.Unmet([], recipe.Options.AllowedBootDiagnostics));
        Assert.Contains("assembly-level pattern", unmet, StringComparison.Ordinal);
        Assert.Contains("declared on assembly", unmet, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmet_Should_NameOnlyTheClassLevelRules_When_TheClassExcludesTheAssemblyModsAndTheBootLoggedNothing()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(VanillaWithRequiredRulesScenario));

        IReadOnlyList<string> unmet = BootDiagnosticsAllowlist.Unmet([], recipe.Options.AllowedBootDiagnostics);

        Assert.Equal(2, unmet.Count);
        Assert.All(unmet, line => Assert.Contains("declared on class", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Map_Should_KeepClassLevelRequiredAndCount_When_TheClassExcludesTheAssemblyMods()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(VanillaWithRequiredRulesScenario));

        AllowedBootDiagnostic required = recipe.Options.AllowedBootDiagnostics.Single(a => a.MessagePattern == "own required");
        AllowedBootDiagnostic counted = recipe.Options.AllowedBootDiagnostics.Single(a => a.MessagePattern == "own counted");
        Assert.True(required.Required);
        Assert.Equal(3, counted.Count);
        Assert.False(recipe.Options.AllowedBootDiagnostics.Single(a => a.MessagePattern == "assembly-level pattern").Required);
    }

    [Fact]
    public void Map_Should_CarryANegativeCountThrough_When_TheAttributeDeclaresOne()
    {
        // 0 is the attribute's "unset"; anything else goes to the strict check, which rejects a
        // value below 1 with the rule's own declaration site named.
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NegativeCountScenario));

        Assert.Equal(-1, recipe.Options.AllowedBootDiagnostics[^1].Count);
    }

    [Fact]
    public void ReadCompiledGameVersion_Should_ReturnTheStampedVersion_When_TheAssemblyCarriesOne()
    {
        Assembly assembly = BuildAssembly(("Atlas.CompiledGameVersion", "1.22.7"), ("Other.Key", "x"));

        CompiledGameVersion compiled = AttributeMapper.ReadCompiledGameVersion(assembly);

        Assert.Equal("1.22.7", compiled.Version);
        Assert.False(compiled.Required);
    }

    [Fact]
    public void ReadCompiledGameVersion_Should_ReturnNoVersion_When_NoStampIsPresent()
    {
        Assembly assembly = BuildAssembly(("Other.Key", "1.22.7"));

        Assert.Null(AttributeMapper.ReadCompiledGameVersion(assembly).Version);
    }

    [Fact]
    public void ReadCompiledGameVersion_Should_ReturnNoVersion_When_TheStampIsEmpty()
    {
        Assembly assembly = BuildAssembly(("Atlas.CompiledGameVersion", string.Empty));

        Assert.Null(AttributeMapper.ReadCompiledGameVersion(assembly).Version);
    }

    [Fact]
    public void ReadCompiledGameVersion_Should_BeRequired_When_TheAssemblyDeclaresTheAttribute()
    {
        Assembly assembly = BuildAssembly(requireAttribute: true, ("Atlas.CompiledGameVersion", "1.21.7"));

        CompiledGameVersion compiled = AttributeMapper.ReadCompiledGameVersion(assembly);

        Assert.Equal("1.21.7", compiled.Version);
        Assert.True(compiled.Required);
    }

    [Fact]
    public void ReadCompiledGameVersion_Should_BeRequiredWithoutAVersion_When_OnlyTheAttributeIsDeclared()
    {
        Assembly assembly = BuildAssembly(requireAttribute: true);

        CompiledGameVersion compiled = AttributeMapper.ReadCompiledGameVersion(assembly);

        Assert.Null(compiled.Version);
        Assert.True(compiled.Required);
    }

    [Fact]
    public void Map_Should_CarryTheAssemblysCompiledGameVersion_When_BuildingTheRecipe()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Equal(AttributeMapper.ReadCompiledGameVersion(typeof(NoAttributeScenario).Assembly), recipe.CompiledGameVersion);
    }

    private static Assembly BuildAssembly(params (string Key, string Value)[] metadata)
        => BuildAssembly(requireAttribute: false, metadata);

    private static Assembly BuildAssembly(bool requireAttribute, params (string Key, string Value)[] metadata)
    {
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("AtlasScenarioStampProbe." + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        ConstructorInfo metadataCtor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        foreach ((string key, string value) in metadata)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(metadataCtor, [key, value]));
        }

        if (requireAttribute)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AtlasRequireCompiledGameVersionAttribute).GetConstructor(Type.EmptyTypes)!, []));
        }

        return builder;
    }

    private class NoAttributeScenario
    {
    }

    [AtlasAllowBootDiagnostic("required one", Required = true)]
    [AtlasAllowBootDiagnostic("counted two", Count = 2)]
    private class RequiredAndCountScenario
    {
    }

    [AtlasAllowBootDiagnostic("negative", Count = -1)]
    private class NegativeCountScenario
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

    [AtlasWorld(ExcludeAssemblyMods = true, Mods = new[] { "class-mod.dll" })]
    [AtlasAllowBootDiagnostic("own required", Required = true)]
    [AtlasAllowBootDiagnostic("own counted", Count = 3)]
    private class VanillaWithRequiredRulesScenario
    {
    }

    [AtlasAllowBootDiagnostic("class-level pattern", Level = "Warning", Source = "mymod")]
    private class AllowedDiagnosticsScenario
    {
    }
}
