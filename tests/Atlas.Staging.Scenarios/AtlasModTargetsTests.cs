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
