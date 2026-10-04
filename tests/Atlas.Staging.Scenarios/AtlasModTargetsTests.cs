using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Atlas.Staging.Scenarios;

/// <summary>Runs build/Atlas.E2E.targets under a real msbuild over throwaway projects, for what
/// the booted scenarios cannot see: what a consumer without a folder mod pays, and the build
/// errors. No server is started. The targets file reaches the build output through this
/// project's csproj, and each test imports it by path the way a consumer's csproj does.</summary>
[Trait("Category", "E2E")]
public sealed class AtlasModTargetsTests : IDisposable
{
    // The assembly's own folder, not AppContext.BaseDirectory: a booted host moves the latter.
    private static readonly string TargetsFile = Path.Combine(
        Path.GetDirectoryName(typeof(AtlasModTargetsTests).Assembly.Location)!, "Atlas.E2E.targets");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("atlas-targets-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_Should_ReadNoFilesOfTheConsumer_When_NoReferenceIsAFolderMod(bool withDllMod)
    {
        // A consumer's own folder holds files the staging must never pick up: with no folder mod
        // the item batch is empty, and a bare "**" include there walks the consumer's whole
        // project directory, bin and obj included, on every build of every package consumer.
        string consumer = withDllMod
            ? CreateConsumer("Consumer", CreateMod("DllMod", "DllMod", folderMod: false))
            : CreateConsumer("Consumer");
        File.WriteAllText(Path.Combine(consumer, "modinfo.json"), "{}");
        Directory.CreateDirectory(Path.Combine(consumer, "assets"));
        File.WriteAllText(Path.Combine(consumer, "assets", "own.json"), "{}");

        (int exitCode, string output) = Dotnet(consumer, "build", "-t:Build", "-getItem:_AtlasModBuildFile,_AtlasModProjectFile");

        Assert.True(exitCode == 0, output);
        using JsonDocument json = JsonDocument.Parse(output[output.IndexOf('{')..]);
        JsonElement items = json.RootElement.GetProperty("Items");
        Assert.All(items.EnumerateObject(), item => Assert.Empty(item.Value.EnumerateArray()));
    }

    [Fact]
    public void Build_Should_FailWithAtlas002NamingOnlyTheClashingProjects_When_TwoFolderModsShareAnAssemblyName()
    {
        string first = CreateMod("First", "Shared");
        string second = CreateMod("Second", "Shared");
        string third = CreateMod("Third", "Distinct");
        string consumer = CreateConsumer("Consumer", first, second, third);

        (int exitCode, string output) = Dotnet(consumer, "build");

        // Every project built before the error shows up in the output; only the error line says
        // which ones the target blamed.
        string error = output.Split('\n').First(line => line.Contains("error ATLAS002", StringComparison.Ordinal));
        Assert.NotEqual(0, exitCode);
        Assert.Contains("'Shared'", error);
        Assert.Contains(Path.Combine(first, "First.csproj"), error);
        Assert.Contains(Path.Combine(second, "Second.csproj"), error);
        Assert.DoesNotContain("Third.csproj", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("global")]
    public void Build_Should_StampTheGameVersion_When_TheApiReferenceIsInTheGlobalNamespace(string? aliases)
    {
        string consumer = CreateApiConsumer("Consumer", aliases);

        (int exitCode, string output) = Dotnet(consumer, "build", "-t:Build", "-getItem:Compile");

        Assert.True(exitCode == 0, output);
        Assert.Contains(StampFileName("Consumer"), CompileItems(output));
    }

    [Theory]
    [InlineData("vs")]
    [InlineData("global,vs")]
    public void Build_Should_SkipTheGameVersionStampAndStillBuild_When_TheApiReferenceHasAnAliasOtherThanGlobal(string aliases)
    {
        // The stamp is a global:: reference: against an extern-aliased VintagestoryAPI it would not
        // compile. Skipped instead, so the build goes through and the boot line says the scenarios
        // carry no compiled game version. A list is skipped too, even one that holds global.
        string consumer = CreateApiConsumer("Consumer", aliases);

        (int exitCode, string output) = Dotnet(consumer, "build", "-t:Build", "-getItem:Compile");

        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain(StampFileName("Consumer"), CompileItems(output));
    }

    private static (int ExitCode, string Output) Dotnet(string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args.Concat(["-nodeReuse:false", "-p:UseSharedCompilation=false", "-nologo"]))
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static string StampFileName(string project) => $"{project}.AtlasGameVersion.g.cs";

    // The Compile items a build ended with, the stamp's generated file among them when it stamped.
    private static string[] CompileItems(string output)
    {
        using JsonDocument json = JsonDocument.Parse(output[output.IndexOf('{')..]);
        return json.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!)
            .Select(Path.GetFileName)
            .ToArray()!;
    }

    // A consumer that references VintagestoryAPI itself, with the given Aliases metadata (none
    // when null), and reads the game version through that reference the way a scenario would.
    private string CreateApiConsumer(string name, string? aliases)
    {
        string dir = Path.Combine(_root.FullName, name);
        Directory.CreateDirectory(dir);
        string aliasAttribute = aliases is null ? string.Empty : $" Aliases=\"{aliases}\"";
        string csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="VintagestoryAPI"{aliasAttribute}>
                  <HintPath>$(VINTAGE_STORY)/VintagestoryAPI.dll</HintPath>
                </Reference>
                <Compile Include="Probe.cs" />
              </ItemGroup>
              <Import Project="{TargetsFile}" />
            </Project>
            """;
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), csproj);
        bool aliased = aliases is not null and not "global";
        string source = aliased
            ? "extern alias vs;\npublic static class Probe { public const string Version = vs::Vintagestory.API.Config.GameVersion.ShortGameVersion; }\n"
            : "public static class Probe { public const string Version = global::Vintagestory.API.Config.GameVersion.ShortGameVersion; }\n";
        File.WriteAllText(Path.Combine(dir, "Probe.cs"), source);
        return dir;
    }

    private string CreateMod(string name, string assemblyName, bool folderMod = true)
    {
        string dir = Path.Combine(_root.FullName, name);
        Directory.CreateDirectory(dir);
        string csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>{assemblyName}</AssemblyName>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), csproj);
        if (folderMod)
        {
            File.WriteAllText(Path.Combine(dir, "modinfo.json"), "{}");
        }

        return dir;
    }

    private string CreateConsumer(string name, params string[] modDirs)
    {
        string dir = Path.Combine(_root.FullName, name);
        Directory.CreateDirectory(dir);
        string references = string.Concat(modDirs.Select(modDir =>
            $"""
                <ProjectReference Include="{Path.Combine(modDir, Path.GetFileName(modDir) + ".csproj")}">
                  <AtlasMod>true</AtlasMod>
                </ProjectReference>

            """));
        string csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
            {references}  </ItemGroup>
              <Import Project="{TargetsFile}" />
            </Project>
            """;
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), csproj);
        return dir;
    }
}
