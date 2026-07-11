using System.Reflection;
using Atlas.Internal.Rollback;
using Atlas.XUnit;
using Atlas.XUnit.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.Engine.Tests;

/// <summary>Covers the stage 3 mod cooperation contract end to end (spec
/// docs/specs/2026-07-11-rollback-stage3-mod-cooperation.md), against a REAL cooperating mod:
/// RollbackHookFixtureMod, a staged dll loaded by the game's own ModLoader, holding
/// registry-style in-memory state seeded from SaveGame moddata (a deliberate miniature of
/// Manifold's dimension registry). The contract is exercised exactly as a shipping mod would:
/// through the engine event bus, by event name, with no shared Atlas assembly. The first test
/// pins the documented boundary honestly: WITHOUT the hook, a rollback desyncs the mod's
/// registry from its restored manifest. The others prove the hook resyncs it, that a throwing
/// handler degrades fail-closed under <see cref="RollbackDegradeReason.ModHookFailed"/> (and
/// fails the scenario under strict isolation, through the full xUnit pipeline), and that the
/// restored hook fires at the spec's exact point: after the SaveGame restore, before any chunk
/// column reload, carrying the versioned payload.</summary>
[Trait("Category", "E2E")]
public class RollbackHookTests
{
    private const string FixtureModDll = "RollbackHookFixtureMod.dll";
    private const string MarkerBlock = "game:soil-medium-normal";
    private const string ModDataKey = "atlas-hook-order-test";

    private static readonly byte[] ExpectedHookModData = [1];
    private static readonly int[] ExpectedFirstRestoreCounts = [1];
    private static readonly int[] ExpectedSecondRestoreCounts = [1, 2];

    /// <summary>The test project's own output directory. Deliberately NOT
    /// <c>AppContext.BaseDirectory</c>: the first host boot in the process redirects that to the
    /// game install, and the fixture mod dll lives next to the test assembly.</summary>
    private static string OutputDirectory => Path.GetDirectoryName(typeof(RollbackHookTests).Assembly.Location)!;

    [Fact]
    public async Task Rollback_Should_DesyncModRegistryFromRestoredSaveGame_When_TheModDoesNotUseTheHook()
    {
        // The documented hard boundary, pinned honestly (the Manifold Ephemeral shape): the mod
        // never resyncs, so its in-memory registry must be observed DESYNCED after the rollback.
        await using var host = NewFixtureHost();
        await host.StartAsync();
        await WaitForPregenAsync(host);

        RollbackAttempt capture = await host.TryRollbackWorldAsync();
        Assert.True(capture.Succeeded, $"capture degraded: {capture.DegradeDetail}");

        // Ephemeral registration: in-memory registry and allocator only, never written to the
        // SaveGame manifest.
        await host.RunScenarioAsync(async world =>
        {
            CommandResult registered = await world.ExecuteCommand("/rollbackfx register temp1 ephemeral");
            Assert.True(registered.Ok, registered.Message);
            Assert.Equal("registered temp1=10", registered.Message);
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback (restore) failed");

        await host.RunScenarioAsync(async world =>
        {
            // The restored SaveGame no longer knows temp1 (correct), but the mod still believes
            // in it: the registry is desynced, and re-registering the code is refused as a
            // duplicate. This is the boundary the atlas:rollback:restored hook exists to close.
            CommandResult state = await world.ExecuteCommand("/rollbackfx state");
            Assert.Contains("registry:[temp1=10]", state.Message);
            Assert.Contains("manifest:[]", state.Message);

            CommandResult again = await world.ExecuteCommand("/rollbackfx register temp1 ephemeral");
            Assert.False(again.Ok, "re-registering temp1 succeeded, but the desynced registry should refuse it");
            Assert.Contains("duplicate: 'temp1' is already registered", again.Message);
        });
    }

    [Fact]
    public async Task Rollback_Should_ResyncModRegistryFromRestoredSaveGame_When_TheModUsesTheRestoredHook()
    {
        await using var host = NewFixtureHost();
        await host.StartAsync();
        await WaitForPregenAsync(host);

        // The cooperating configuration: the mod's restored-hook handler re-runs its boot
        // hydrate (rebuild registry and allocator from the restored manifest).
        await host.RunScenarioAsync(async world =>
        {
            Assert.True((await world.ExecuteCommand("/rollbackfx hook resync")).Ok);

            // A durable registration BEFORE the capture: persisted in the manifest, so the
            // restored SaveGame carries it and the resync must bring it back, same id.
            CommandResult keeper = await world.ExecuteCommand("/rollbackfx register keeper");
            Assert.Equal("registered keeper=10", keeper.Message);
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");

        // Pollute both directions of the desync: an ephemeral registration the restored world
        // must forget, and a removal the restored manifest must undo (the Lifecycle shape).
        await host.RunScenarioAsync(async world =>
        {
            Assert.Equal("registered temp1=11", (await world.ExecuteCommand("/rollbackfx register temp1 ephemeral")).Message);
            Assert.Equal("removed keeper=10", (await world.ExecuteCommand("/rollbackfx remove keeper")).Message);
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback (restore) failed");

        await host.RunScenarioAsync(async world =>
        {
            // Resynced: keeper is back under its persisted id, temp1 is gone from memory too,
            // and both the code and the id are reusable again.
            CommandResult state = await world.ExecuteCommand("/rollbackfx state");
            Assert.Contains("registry:[keeper=10]", state.Message);
            Assert.Contains("manifest:[keeper=10]", state.Message);

            CommandResult again = await world.ExecuteCommand("/rollbackfx register temp1 ephemeral");
            Assert.True(again.Ok, again.Message);
            Assert.Equal("registered temp1=11", again.Message);
        });
    }

    [Fact]
    public async Task Rollback_Should_DegradeWithModHookFailed_When_ARestoredHandlerThrows()
    {
        await using var host = NewFixtureHost();
        await host.StartAsync();
        await WaitForPregenAsync(host);

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");
        await host.RunScenarioAsync(async world =>
            Assert.True((await world.ExecuteCommand("/rollbackfx hook throw")).Ok));

        var stderr = new StringWriter();
        TextWriter realStderr = Console.Error;
        RollbackAttempt attempt;
        try
        {
            Console.SetError(stderr);
            attempt = await host.TryRollbackWorldAsync();
        }
        finally
        {
            Console.SetError(realStderr);
        }

        // Fail closed, with the classified reason and the mod's own exception in the detail.
        Assert.False(attempt.Succeeded, "the rollback succeeded despite a throwing restored-hook handler");
        Assert.Equal(RollbackDegradeReason.ModHookFailed, attempt.DegradeReason);
        Assert.Contains("atlas:rollback:restored", attempt.DegradeDetail);
        Assert.Contains("InvalidOperationException: rollbackfx: simulated handler failure", attempt.DegradeDetail);
        Assert.Contains("mod rollback hook failed", stderr.ToString());
    }

    [Fact]
    public async Task StrictIsolation_Should_FailTheScenario_When_AModRestoredHookHandlerThrows()
    {
        // Through the full xUnit pipeline (case runner, invoker, registry, host), exactly like
        // IsolationObservabilityTests: the probe class's host stages the fixture mod via its
        // [AtlasWorld] metadata, the capture happens on the first rollback request, then the
        // mod's handler is armed to throw and the strict scenario must fail with the reason.
        ServerHost host = await HostRegistry.GetOrCreateAsync(typeof(StrictHookProbeScenarios));
        await WaitForPregenAsync(host);
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");
        await host.RunScenarioAsync(async world =>
            Assert.True((await world.ExecuteCommand("/rollbackfx hook throw")).Ok));

        IReadOnlyList<IMessageSinkMessage> messages = await RunScenarioCaseAsync(
            typeof(StrictHookProbeScenarios),
            nameof(StrictHookProbeScenarios.Scenario_Should_NotRun));

        ITestFailed failed = Assert.Single(messages.OfType<ITestFailed>());
        Assert.Equal("Atlas.Api.AtlasIsolationException", Assert.Single(failed.ExceptionTypes));
        string message = Assert.Single(failed.Messages);
        Assert.Contains("StrictIsolation", message);
        Assert.Contains("mod rollback hook failed", message);
        Assert.Contains("rollbackfx: simulated handler failure", message);
        Assert.False(StrictHookProbeScenarios.BodyRan, "the scenario body ran despite the strict failure");

        // Strictness changes visibility, not safety: the degrade already recycled the probe
        // host, so the class is on a clean world (with a fresh mod instance, hook mode off).
        ServerHost replacement = await HostRegistry.GetOrCreateAsync(typeof(StrictHookProbeScenarios));
        Assert.NotSame(host, replacement);
    }

    [Fact]
    public async Task RestoredHook_Should_ObserveRestoredSaveGamePreReload_And_CarryTheVersionedPayload()
    {
        // The ordering promise, asserted from inside a handler on the real bus: at hook time the
        // SaveGame is already restored, no chunk column is loaded yet. No mod needed here; a
        // listener registered through the same engine api pins the same contract.
        string baseDir = OutputDirectory;
        await using var host = new ServerHost(new WorldOptions(), Array.Empty<string>(), baseDir);
        await host.StartAsync();

        BlockPos marker = null!;
        int capturedVersion = 0;
        int capturedGeneration = 0;
        int restoredVersion = 0;
        int restoredGeneration = 0;
        var restoredCounts = new List<int>();
        byte[]? modDataAtHookTime = null;
        bool markerChunkLoadedAtHookTime = true;

        await host.RunScenarioAsync(async world =>
        {
            marker = world.Spawn.Offset(3, 1, 0);
            world.SetBlock(MarkerBlock, marker);
            world.Api.WorldManager.SaveGame.StoreData(ModDataKey, new byte[] { 1 });

            world.Api.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) =>
                {
                    var payload = (ITreeAttribute)data;
                    capturedVersion = payload.GetInt("version");
                    capturedGeneration = payload.GetInt("generation");
                },
                filterByEventName: "atlas:rollback:captured");
            world.Api.Event.RegisterEventBusListener(
                (string eventName, ref EnumHandling handling, IAttribute data) =>
                {
                    var payload = (ITreeAttribute)data;
                    restoredVersion = payload.GetInt("version");
                    restoredGeneration = payload.GetInt("generation");
                    restoredCounts.Add(payload.GetInt("restoreCount"));
                    modDataAtHookTime = world.Api.WorldManager.SaveGame.GetData(ModDataKey);
                    markerChunkLoadedAtHookTime =
                        world.Api.World.BlockAccessor.GetChunkAtBlockPos(marker) != null;
                },
                filterByEventName: "atlas:rollback:restored");
            await world.Ticks(2);
        });

        // Capture: the captured event fires once, with the schema version and a generation.
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "capture failed");
        Assert.Equal(1, capturedVersion);
        Assert.True(capturedGeneration > 0, "the captured payload carried no generation");
        Assert.Empty(restoredCounts);

        // Pollute the moddata the handler will read back at hook time.
        await host.RunScenarioAsync(async world =>
        {
            world.Api.WorldManager.SaveGame.StoreData(ModDataKey, new byte[] { 9 });
            await world.Ticks(1);
        });

        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "rollback (restore) failed");

        // The spec's ordering promise: the handler saw the RESTORED moddata (the capture-time
        // bytes, not the pollution) while the marker's chunk column was NOT loaded (the restore
        // pushed the hook after the SaveGame restore and before any column reload).
        Assert.Equal(ExpectedHookModData, modDataAtHookTime);
        Assert.False(markerChunkLoadedAtHookTime, "a chunk column was already loaded when the restored hook fired");
        Assert.Equal(1, restoredVersion);
        Assert.Equal(capturedGeneration, restoredGeneration);
        Assert.Equal(ExpectedFirstRestoreCounts, restoredCounts);

        // After the restore completes, the world is whole again: the marker is back, reloaded.
        await host.RunScenarioAsync(world =>
        {
            Assert.Equal(MarkerBlock, world.BlockAt(marker).Code.ToString());
            return Task.CompletedTask;
        });

        // A second restore of the same generation increments the restore count only.
        Assert.True((await host.TryRollbackWorldAsync()).Succeeded, "second restore failed");
        Assert.Equal(capturedGeneration, restoredGeneration);
        Assert.Equal(ExpectedSecondRestoreCounts, restoredCounts);
    }

    /// <summary>Boots a host with the rollback-hook fixture mod staged, the way a consumer
    /// stages any mod-under-test.</summary>
    private static ServerHost NewFixtureHost()
        => new(new WorldOptions(), new[] { FixtureModDll }, OutputDirectory);

    /// <summary>Waits until the fixture mod finished its boot-time mini-dimension
    /// pregeneration (it defers creation by a few ticks until the spawn map chunk exists).</summary>
    private static Task WaitForPregenAsync(ServerHost host)
        => host.RunScenarioAsync(async world =>
        {
            for (int i = 0; i < 200; i++)
            {
                CommandResult state = await world.ExecuteCommand("/rollbackfx state");
                Assert.True(state.Ok, state.Message);
                if (state.Message.Contains("pregen:true", StringComparison.Ordinal))
                {
                    return;
                }

                await world.Ticks(10);
            }

            Assert.Fail("the fixture mod's boot-time mini-dimension pregeneration never completed");
        });

    /// <summary>Runs one probe scenario through the real Atlas xUnit pipeline with
    /// RollbackWorld + StrictIsolation, collecting every message the runner reports (same
    /// nested-case shape as <see cref="IsolationObservabilityTests"/>).</summary>
    /// <param name="probeClass">The probe scenario class.</param>
    /// <param name="methodName">The scenario method to run.</param>
    /// <returns>The messages the pipeline queued, in order.</returns>
    private static async Task<IReadOnlyList<IMessageSinkMessage>> RunScenarioCaseAsync(
        Type probeClass, string methodName)
    {
        var diagnosticSink = new NullDiagnosticSink();
        var testCase = new AtlasTestCase(
            diagnosticSink,
            Xunit.Sdk.TestMethodDisplay.ClassAndMethod,
            Xunit.Sdk.TestMethodDisplayOptions.None,
            BuildTestMethod(probeClass, methodName),
            freshWorld: false,
            rollbackWorld: true,
            restartWorld: false,
            strictIsolation: true,
            timeoutMs: 60_000);

        using var bus = new SpyMessageBus();
        await testCase.RunAsync(
            diagnosticSink, bus, Array.Empty<object>(), new ExceptionAggregator(), new CancellationTokenSource());
        return bus.Messages;
    }

    /// <summary>Builds the xUnit test-method object graph for a probe scenario, the same shape
    /// the real discoverer produces.</summary>
    /// <param name="probeClass">The probe scenario class.</param>
    /// <param name="methodName">The scenario method.</param>
    /// <returns>The test method.</returns>
    private static TestMethod BuildTestMethod(Type probeClass, string methodName)
    {
        MethodInfo method = probeClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"probe method '{methodName}' not found on '{probeClass}'");
        var testAssembly = new TestAssembly(Reflector.Wrap(probeClass.Assembly));
        var collection = new TestCollection(testAssembly, null, "Atlas rollback hook probes");
        var testClass = new TestClass(collection, Reflector.Wrap(probeClass));
        return new TestMethod(testClass, Reflector.Wrap(method));
    }

    // Private probe on purpose (xUnit only discovers public classes, so the outer test run
    // never executes it directly); the [AtlasScenario] attribute must stay because
    // XunitTestCase.Initialize reads the FactAttribute off the method even for manually built
    // cases. Same pattern and rationale as IsolationObservabilityTests.
#pragma warning disable xUnit1000

    /// <summary>Probe for the strict-isolation-on-mod-hook-failure test. Its host stages the
    /// fixture mod through the class-level world metadata.</summary>
    [AtlasWorld(Mods = new[] { FixtureModDll })]
    private sealed class StrictHookProbeScenarios : AtlasScenarioBase
    {
        /// <summary>Gets a value indicating whether the scenario body executed: strict isolation
        /// must fail the scenario BEFORE its body runs.</summary>
        public static bool BodyRan { get; private set; }

        [AtlasScenario(RollbackWorld = true, StrictIsolation = true)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Blocker Code Smell",
            "S2699:Tests should include assertions",
            Justification = "Probe scenario driven by the strict-hook-failure test, which asserts its outcome externally (AtlasIsolationException before the body, BodyRan stays false). The body must stay assertion-free so a guard regression surfaces in the outer test instead of being masked by a different failure.")]
        public async Task Scenario_Should_NotRun()
        {
            BodyRan = true;
            await World.Ticks(1);
        }
    }

#pragma warning restore xUnit1000

    /// <summary>Message bus spy: collects every message the pipeline queues.</summary>
    private sealed class SpyMessageBus : IMessageBus
    {
        private readonly List<IMessageSinkMessage> _messages = [];

        public IReadOnlyList<IMessageSinkMessage> Messages => _messages;

        public bool QueueMessage(IMessageSinkMessage message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }

            return true;
        }

        public void Dispose()
        {
            // Nothing to release; the spy only holds managed state.
        }
    }

    /// <summary>Diagnostic sink that swallows everything (the probe's diagnostics are noise).</summary>
    private sealed class NullDiagnosticSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message) => true;
    }
}
