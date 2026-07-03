using System.Reflection;
using Atlas.XUnit;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.XUnit;

public class AttributeMappingTests
{
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

        Assert.Equal(new[] { "assembly-mod.dll", "class-mod.dll" }, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_UseOnlyAssemblyMods_When_ClassHasNoExtraMods()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        Assert.Equal(new[] { "assembly-mod.dll" }, recipe.ModPaths);
    }

    [Fact]
    public void Map_Should_UseAssemblyLocationDirectory_When_ResolvingModBaseDir()
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(typeof(NoAttributeScenario));

        string expected = Path.GetDirectoryName(typeof(NoAttributeScenario).Assembly.Location)!;
        Assert.Equal(expected, recipe.ModBaseDir);
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
}
