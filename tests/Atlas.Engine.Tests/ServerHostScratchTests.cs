using System.Text.Json;

namespace Atlas.Engine.Tests;

/// <summary>Pins that a <see cref="ServerHost"/> built outside the registry, as the engine tests
/// build theirs, deletes the scratch directory of a booted server at a clean dispose (issue #182):
/// the world save, the logs and the staged mods the engine wrote, not a stand-in file. The sweep
/// decision and its keep reasons are in the pure suite (<c>ServerHostScratchSweepTests</c>), the
/// crash and abandoned-thread keeps in <c>CrashSurfacingTests</c> and
/// <c>TeardownDiagnosticsTests</c>, where those hosts already exist. A booted host also writes
/// the witness file that says which run made its scratch directory.</summary>
[Trait("Category", "E2E")]
public class ServerHostScratchTests
{
    [Fact]
    public async Task DisposeAsync_Should_DeleteTheScratch_When_ABootedHostEndsClean()
    {
        ServerHost host = TestHosts.New();
        await host.StartAsync();
        string dataPath = host.DataPath;
        Assert.True(Directory.Exists(dataPath), "the running host's scratch must exist");

        await host.DisposeAsync();

        Assert.False(Directory.Exists(dataPath), $"the clean host's scratch '{dataPath}' must be deleted");
    }

    [Fact]
    public async Task StartAsync_Should_WriteTheWitnessFile_When_TheHostBoots()
    {
        ServerHost host = TestHosts.New();
        try
        {
            await host.StartAsync();

            using JsonDocument witness = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(host.DataPath, ScratchWitness.FileName)));
            JsonElement root = witness.RootElement;
            Assert.Equal(Environment.ProcessId, root.GetProperty("processId").GetInt32());
            Assert.Matches("^[0-9a-f]{32}$", root.GetProperty("runId").GetString());

            // A host built outside the registry has no scenario class, so no test assembly.
            Assert.Equal(JsonValueKind.Null, root.GetProperty("testAssembly").ValueKind);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }
}
