using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Atlas.Api;
using Atlas.Internal.Bootstrap;
using Atlas.Internal.Rollback;
using Atlas.Internal.Scheduling;
using Atlas.Internal.Staging;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Owns one embedded headless server: dedicated game thread, pump, lifecycle.</summary>
internal sealed class ServerHost : IAsyncDisposable
{
    /// <summary>Bound on the dispose-time wait for the boot's background server-assets build
    /// (see <see cref="WaitForAssetsBuildToSettle"/>). Generous on purpose: the build takes 1-3
    /// seconds bare and single-digit seconds under coverage instrumentation, and a timeout here
    /// risks crashing the whole test process.</summary>
    private static readonly TimeSpan AssetsBuildSettleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>One-time latch for <see cref="WarnAssetsBuildProbeMissingOnce"/>.</summary>
    private static int assetsBuildProbeWarned;

    private readonly WorldOptions _options;
    private readonly IReadOnlyList<string> _modPaths;
    private readonly string _modBaseDir;
    private readonly IReadOnlyList<DataFileSeed> _dataFiles;
    private readonly string _dataPath = Path.Combine(
        Path.GetTempPath(), "atlas", Guid.NewGuid().ToString("N"));

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _gameThreadJoinTimeout;

    // Owned by the host, not the per-scenario WorldSession: joined test players outlive the
    // scenario that joined them (they stay connected for the host's lifetime), so the
    // duplicate-name guard has to remember names across scenarios sharing this host's world.
    private readonly HashSet<string> _joinedPlayerNames = [];

    private Thread? _gameThread;
    private GameThreadScheduler? _scheduler;
    private TickSource? _ticks;
    private ICoreServerAPI? _api;
    private ServerMain? _server;
    private volatile Exception? _crash;

    // World snapshot for rollback-isolated scenarios: captured lazily by the first rollback
    // request on this host, so classes that never roll back pay nothing. A recycle replaces the
    // whole host, which resets this along with everything else.
    private IWorldSnapshot? _worldSnapshot;

    /// <summary>Initializes a new instance of the <see cref="ServerHost"/> class.</summary>
    /// <param name="options">World configuration for the embedded server.</param>
    /// <param name="modPaths">Paths to mods-under-test to stage alongside the bridge.</param>
    /// <param name="modBaseDir">Base directory used to resolve relative mod and data file paths.</param>
    /// <param name="dataFiles">Files to seed into the scratch data path before the server boots,
    /// so mods reading configuration during startup (e.g. <c>api.LoadModConfig</c> in
    /// <c>StartServerSide</c>) see them.</param>
    /// <param name="gameThreadJoinTimeout">How long <see cref="DisposeAsync"/> waits for the game
    /// thread before abandoning it. Test hook: only teardown-diagnostics tests shorten it; real
    /// consumers keep the 30 second default.</param>
    public ServerHost(
        WorldOptions options,
        IReadOnlyList<string> modPaths,
        string modBaseDir,
        IReadOnlyList<DataFileSeed>? dataFiles = null,
        TimeSpan? gameThreadJoinTimeout = null)
    {
        _options = options;
        _modPaths = modPaths;
        _modBaseDir = modBaseDir;
        _dataFiles = dataFiles ?? [];
        _gameThreadJoinTimeout = gameThreadJoinTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>Gets the number of ticks raised so far, or zero before the host is ready.</summary>
    /// <remarks>Safe to read from any thread: backed by <see cref="TickSource.TickCount"/>, which uses
    /// a volatile read. Intended for diagnostics (e.g. a watchdog timeout message) where a value that
    /// is stale by a tick or two is acceptable.</remarks>
    public int CurrentTick => _ticks?.TickCount ?? 0;

    /// <summary>Gets this host's scratch data path (world save, logs, staged mods).</summary>
    /// <remarks>Test hook and diagnostics aid: lets a test harvest artifacts the embedded server
    /// wrote, e.g. the world save a graceful teardown persisted.</remarks>
    internal string DataPath => _dataPath;

    /// <summary>Gets the game thread, or <see langword="null"/> before <see cref="StartAsync"/>.</summary>
    /// <remarks>Test hook: a test that deliberately lets the <see cref="DisposeAsync"/> join expire
    /// must still wait for the real teardown afterwards, so the abandoned thread's late
    /// <c>ServerMain.Dispose()</c> cannot null process-wide engine statics under the next test's
    /// host (the issue #8 hazard).</remarks>
    internal Thread? GameThread => _gameThread;

    /// <summary>Gets the crash captured by the game thread, if the embedded server died.</summary>
    /// <remarks>Belt-and-suspenders for callers (e.g. the xUnit invoker) that observe a different
    /// symptom of a crash, such as a watchdog timeout, and want to recover the true root cause.
    /// Always the original game-thread exception: if <c>server.Stop()</c> also throws during crash
    /// teardown, that secondary failure is logged rather than aggregated, so the root cause stays
    /// exactly one level deep under <see cref="ServerCrashedException"/>.</remarks>
    internal Exception? CrashException => _crash;

    /// <summary>Gets a value indicating whether this host has captured a world snapshot yet.
    /// Stays <see langword="false"/> until the first successful <see cref="TryRollbackWorldAsync"/>,
    /// which is what makes the capture lazy: classes that never roll back pay nothing.</summary>
    internal bool HasWorldSnapshot => _worldSnapshot != null;

    /// <summary>Gets or sets the factory that builds this host's <see cref="IWorldSnapshot"/>.
    /// Test seam for the fail-closed fallback: production keeps the default
    /// (<see cref="WorldSnapshot.Create"/>); a test swaps in a factory that throws or returns a
    /// failing snapshot to prove that a capture or restore failure degrades to a full host
    /// recycle instead of corrupting the run.</summary>
    internal Func<ICoreServerAPI, TickSource, IWorldSnapshot> WorldSnapshotFactory { get; set; }
        = WorldSnapshot.Create;

    /// <summary>Spawns the game thread and boots the embedded server.</summary>
    /// <returns>A task that resolves once the bridge API is ready.</returns>
    public Task StartAsync()
    {
        _gameThread = new Thread(GameThreadMain) { Name = "atlas-game", IsBackground = true };
        _gameThread.Start();
        return _ready.Task;
    }

    /// <summary>Runs work on the game thread via the scheduler.</summary>
    /// <param name="work">The work to run, given the live server API and tick source.</param>
    /// <returns>A task that completes when the work completes.</returns>
    /// <exception cref="ServerCrashedException">Thrown when the embedded server died.</exception>
    /// <remarks>Precondition: <see cref="StartAsync"/> must have completed successfully before
    /// calling this method, so that the live server API and scheduler are available.</remarks>
    public async Task RunOnGameThreadAsync(Func<ICoreServerAPI, TickSource, Task> work)
    {
        ThrowIfCrashed();
        try
        {
            await _scheduler!.RunAsync(() => work(_api!, _ticks!)).ConfigureAwait(false);
        }
        finally
        {
            ThrowIfCrashed();
        }
    }

    /// <summary>Runs an author-facing scenario on the game thread, over the world session surface.</summary>
    /// <param name="scenario">The scenario to run, given a live <see cref="IWorldSession"/>.</param>
    /// <returns>A task that completes when the scenario completes.</returns>
    /// <exception cref="ServerCrashedException">Thrown when the embedded server died.</exception>
    /// <remarks>Precondition: <see cref="StartAsync"/> must have completed successfully before
    /// calling this method, so that the live server API and scheduler are available.</remarks>
    public Task RunScenarioAsync(Func<IWorldSession, Task> scenario)
        => RunOnGameThreadAsync(
            (api, ticks) => scenario(new WorldSession(api, _server!, ticks, _joinedPlayerNames, _modBaseDir)));

    /// <summary>Rolls the world back to this host's snapshot, capturing it first if this is the
    /// host's first rollback request (in that case the world is by definition already in the
    /// snapshot state, so nothing is restored).</summary>
    /// <returns>A succeeded <see cref="RollbackAttempt"/> when the caller now has a world in the
    /// snapshot state; a degraded one, carrying the classified
    /// <see cref="RollbackDegradeReason"/> and a one-line detail, when capture or restore failed
    /// for any reason (including engine drift in a future game version), after logging a one-line
    /// warning to stderr. Fail closed: on a degraded attempt the caller must fall back to a full
    /// host recycle.</returns>
    /// <exception cref="AtlasSetupException">Thrown when test players have joined on this host:
    /// stage 1 rollback does not capture or restore player entity state (position, inventory,
    /// stats), so rolling back under them would silently corrupt it. Deliberately NOT part of
    /// the fail-closed fallback: this is an authoring error the scenario must surface, not an
    /// environment failure to paper over. Combining players with rollback is a later stage; use
    /// FreshWorld isolation for scenarios that need both today.</exception>
    /// <exception cref="ServerCrashedException">Thrown when the embedded server died; also kept
    /// out of the fallback so the crash surfaces as the root cause.</exception>
    /// <remarks>Precondition: <see cref="StartAsync"/> must have completed successfully, and no
    /// scenario may be running (the xUnit invoker calls this between scenarios, in the same slot
    /// where FreshWorld recycles the host).</remarks>
    public async Task<RollbackAttempt> TryRollbackWorldAsync()
    {
        if (_joinedPlayerNames.Count > 0)
        {
            throw new AtlasSetupException(
                "World rollback is not supported on a host with joined test players in stage 1: " +
                "the snapshot does not capture or restore player entity state, so rolling back " +
                "would silently corrupt it. Use [AtlasScenario(FreshWorld = true)] for scenarios " +
                "that need both test players and isolation (players + rollback is a later stage).");
        }

        try
        {
            if (_worldSnapshot is { } snapshot)
            {
                await RunOnGameThreadAsync((_, _) => snapshot.RestoreAsync()).ConfigureAwait(false);
                return RollbackAttempt.Success();
            }

            await RunOnGameThreadAsync(async (api, ticks) =>
            {
                IWorldSnapshot created = WorldSnapshotFactory(api, ticks);
                await created.CaptureAsync().ConfigureAwait(true);
                _worldSnapshot = created;
            }).ConfigureAwait(false);
            return RollbackAttempt.Success();
        }
        catch (Exception ex) when (ex is not ServerCrashedException)
        {
            RollbackDegradeReason reason = RollbackDegrade.Classify(ex);
            string detail = $"{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}";
            await Console.Error.WriteLineAsync(
                $"[Atlas] world rollback failed ({RollbackDegrade.Describe(reason)}), " +
                $"falling back to a full host recycle: {detail}").ConfigureAwait(false);
            _worldSnapshot = null;
            return RollbackAttempt.Degraded(reason, detail);
        }
    }

    /// <summary>Stops and disposes the embedded server, then joins the game thread.</summary>
    /// <returns>A task that completes when the game thread has exited, or when the bounded join
    /// times out waiting for a wedged game thread.</returns>
    /// <remarks>A normal shutdown observes <see cref="_stop"/> at the top of the pump loop in
    /// <see cref="GameThreadMain"/> and joins in roughly 1-2 seconds. The join bound (30 seconds
    /// by default) only matters when the game thread is wedged inside a single
    /// <c>server.Process()</c> call (the same scenario the scenario watchdog guards against) and
    /// never observes the cancellation. In that case the join gives up and this method returns
    /// without throwing: the thread is created with <c>IsBackground = true</c>, so abandoning it
    /// does not block process exit, and a wedged embedded server cannot be shut down safely from
    /// the outside anyway.</remarks>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_gameThread != null)
        {
            bool joined = await Task.Run(() => _gameThread.Join(_gameThreadJoinTimeout)).ConfigureAwait(false);
            if (!joined)
            {
                // An abandoned teardown keeps running: when its late ServerMain.Dispose() lands,
                // it nulls process-wide engine statics (ServerMain.Logger) under whatever host
                // runs next — the suspected trigger of the issue #8 shutdown NRE. Log it loudly
                // so a later flake in this test process can be correlated back to this timeout.
                await Console.Error.WriteLineAsync(
                    $"[Atlas] game thread did not exit within {_gameThreadJoinTimeout.TotalSeconds:0.#}s " +
                    "and was abandoned; its late teardown may null process-wide engine statics under " +
                    "the next host (see issue #8).").ConfigureAwait(false);
            }
        }

        _stop.Dispose();
    }

    /// <summary>Wraps <see cref="CrashException"/> as the same <see cref="ServerCrashedException"/>
    /// callers observe from <see cref="ThrowIfCrashed"/>, for callers that captured a different
    /// symptom of the crash (e.g. a watchdog timeout) and want to surface the true cause instead.</summary>
    /// <returns>The wrapped crash, or <see langword="null"/> if the host has not crashed.</returns>
    internal ServerCrashedException? WrapCrashIfAny() => _crash is { } crash ? WrapCrash(crash) : null;

    private void ThrowIfCrashed()
    {
        if (_crash != null)
        {
            throw WrapCrash(_crash);
        }
    }

    /// <summary>Wraps a captured crash as the <see cref="ServerCrashedException"/> callers observe,
    /// with a consistent message across every place a crash surfaces.</summary>
    /// <param name="crash">The captured crash.</param>
    /// <returns>The wrapped exception.</returns>
    private ServerCrashedException WrapCrash(Exception crash)
        => new($"Embedded server died; logs: {_dataPath}", crash);

    /// <remarks>Runs on the game thread; this method IS the game thread entry point.</remarks>
    [SuppressMessage(
        "Major Bug",
        "S1696:NullReferenceException should not be caught",
        Justification = "Vintage Story (1.22.2 and 1.22.3) throws NullReferenceException from ServerSystemMonitor.Dispose() on embedded-server shutdown (upstream bug, issue #8); there is no null to test for on our side, the throw happens inside the game's own Dispose.")]
    private void GameThreadMain()
    {
        ServerMain? server = null;
        try
        {
            string install = VsInstall.Locate();

            // Preflight BEFORE Initialize: it redirects AppContext.BaseDirectory to the install
            // directory, and this check targets the consumer test output that the base directory
            // still points at. A VintagestoryAPI.dll copied there without its pdb would otherwise
            // kill the boot in ConfigureEngineStatics with an opaque TypeInitializationException
            // from LoggerBase..cctor (see VerifyApiPdbPresent remarks). Likewise, a test-output
            // copy that has diverged from the install's (VINTAGE_STORY repointed at a different
            // install without rebuilding, issue #49) would mix assemblies and die deep into boot
            // with a cryptic MissingFieldException (see VerifyApiCopyMatchesInstall remarks).
            VsInstall.VerifyApiPdbPresent(AppContext.BaseDirectory);
            VsInstall.VerifyApiCopyMatchesInstall(AppContext.BaseDirectory, install);
            GameEnvironment.Initialize(install);
            Directory.SetCurrentDirectory(install);

            string staging = Path.Combine(_dataPath, "TestMods");

            // Stage the mod-under-test.
            ModStager.Stage(_modPaths, _modBaseDir, staging);

            // Seed declared data files (e.g. ModConfig/*.json) into the scratch data path before
            // the server boots, so mods reading config in StartServerSide already see them.
            DataSeeder.Seed(_dataFiles, _modBaseDir, _dataPath);

            // A prebuilt world save wins over world generation: BootServer pins the engine's save
            // location to this exact file, and the engine loads any save it finds there. Seeded
            // after the raw data files so an explicit SaveFile also wins over a save smuggled in
            // through a data-file seed.
            if (_options.SaveFile is { } saveFile)
            {
                DataSeeder.SeedWorldSave(saveFile, _modBaseDir, _dataPath);
            }

            // Stage AtlasBridge.dll alone into its own folder. It must NOT share a folder with
            // the consumer test project's bin output: that directory is full of non-mod dlls
            // (test framework, mocking libraries, etc.) that the game's ModLoader would also
            // scan. The ModLoader therefore loads a COPY of AtlasBridge.dll, distinct from the
            // engine's own assembly instance, so BridgeRendezvous.Reset() wires up an
            // AppDomain-slot handoff instead of relying on shared statics.
            string bridgeStaging = Path.Combine(_dataPath, "BridgeMod");
            string bridgeSource = typeof(Bridge.BridgeRendezvous).Assembly.Location;
            ModStager.StageBridge(bridgeSource, bridgeStaging);

            Bridge.BridgeRendezvous.Reset();

            _scheduler = GameThreadScheduler.InstallOnCurrentThread();
            _ticks = new TickSource();
            Bridge.BridgeRendezvous.TickFired += _ticks.RaiseTick;

            server = BootServer(staging, bridgeStaging);

            for (int i = 0; i < 100 && !Bridge.BridgeRendezvous.ApiReady.IsCompleted; i++)
            {
                server.Process();
                _scheduler.DrainPending();
            }

            if (!Bridge.BridgeRendezvous.ApiReady.IsCompleted)
            {
                throw new AtlasSetupException(
                    $"Atlas bridge mod did not start. Check the server logs under '{_dataPath}' " +
                    "(the mod loader may have failed to load AtlasBridge.dll or a mod-under-test).");
            }

            _api = Bridge.BridgeRendezvous.ApiReady.Result;
            _server = server;
            _ready.TrySetResult();

            while (!_stop.IsCancellationRequested)
            {
                server.Process();
                _scheduler.DrainPending();
            }

            server.Stop("Atlas scenario class finished", EnumExitMode.SoftExit);
        }
        catch (Exception ex)
        {
            _crash = ex;
            _ready.TrySetException(ex);

            try
            {
                server?.Stop("Atlas host crashed", EnumExitMode.SoftExit);
            }
            catch (Exception stopEx)
            {
                // Swallow: shutdown is best-effort after a crash. The original exception stays
                // the one and only crash, so callers see the root cause one level deep instead
                // of buried in an AggregateException; the stop failure is merely logged.
                Console.Error.WriteLine(
                    $"[Atlas] server.Stop() failed during crash teardown (kept the original crash as the cause): {stopEx}");
            }

            // A scenario may be parked on await World.Ticks(...) (or another TickSource wait)
            // right now, with no more ticks ever coming: the game thread is about to exit. Fault
            // every pending waiter with the true cause (wrapped the same way ThrowIfCrashed wraps
            // it) and drain the scheduler so the scenario task observes it promptly, instead of
            // hanging until the watchdog fires a ScenarioTimeoutException that points away from
            // the real cause. Both may be unset if the crash happened before they were assigned.
            ServerCrashedException crashException = WrapCrash(ex);
            _ticks?.FailAll(crashException);
            _scheduler?.DrainPending();
        }
        finally
        {
            // Single choke point for the boot-time assets-build race, covering BOTH teardown
            // paths (normal Stop above, crash Stop in the catch): every route to Dispose funnels
            // through this finally, and Dispose is the only engine call that nulls the statics
            // the build dereferences (Stop never touches them, verified on 1.22.2), so waiting
            // here is both necessary and sufficient. See WaitForAssetsBuildToSettle.
            if (server != null)
            {
                WaitForAssetsBuildToSettle(server);
            }

            try
            {
                server?.Dispose();
            }
            catch (NullReferenceException ex)
            {
                // Swallow, but keep the evidence: Vintage Story (confirmed on 1.22.2 and 1.22.3)
                // throws NullReferenceException from ServerSystemMonitor.Dispose() when the static
                // ServerMain.Logger was already nulled by another server lifecycle's Dispose()
                // (issue #8). Not a failure of our shutdown logic; the stack goes to stderr so a
                // real occurrence can be correlated with an abandoned-teardown warning above.
                Console.Error.WriteLine(
                    $"[Atlas] server.Dispose() threw the known Vintage Story shutdown NRE (issue #8): {ex}");
            }
        }
    }

    /// <summary>Waits, bounded, for the boot's background server-assets build to have settled
    /// before the caller lets <c>ServerMain.Dispose()</c> null the process-wide engine statics
    /// that build dereferences.</summary>
    /// <param name="server">The embedded server about to be disposed.</param>
    /// <remarks><para>Runs on the game thread, in the teardown finally: the pump loop has already
    /// exited, and the build needs no pumping anyway (the engine queues it on the .NET thread
    /// pool via <c>TyronThreadPool.QueueTask</c>), so plain sleeping is the right wait here.</para>
    /// <para>The race being closed: <c>ServerMain.Launch()</c> queues
    /// <c>BuildServerAssetsPacket</c> on a pool thread; that build reads
    /// <c>ServerMain.ClassRegistry</c> (via <c>api.ClassRegistry</c>) and its catch handlers log
    /// via the static <c>ServerMain.Logger</c>, while its only outer catch handles
    /// <c>ThreadAbortException</c>. <c>ServerMain.Dispose()</c> nulls BOTH statics. A host
    /// disposed while the build is still in flight (a fast test class, wider still under coverage
    /// instrumentation) makes the build NRE on the nulled registry, its catch NRE again on the
    /// nulled logger, and an unhandled exception on a pool thread kills the whole test process.</para>
    /// <para>Completion signal (same state the engine's own <c>WaitOnBuildServerAssetsPacket</c>
    /// polls): the private <c>serverAssetsPacket</c> box has <c>packet != null</c> (non-dedicated,
    /// Atlas's case) or <c>Length != 0</c> (dedicated). The box is initialized inline at
    /// construction, so reading it is safe on any server object, even one whose boot crashed
    /// before <c>Launch()</c> queued the build; in that rare early-crash case the signal never
    /// fires and the bounded timeout is the way out. On timeout this logs one stderr line and
    /// proceeds: a wedged build must not deadlock teardown, and the process crash it risks is no
    /// worse than proceeding used to be.</para></remarks>
    [SuppressMessage(
        "Major Code Smell",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "Boot-validated reflection over the engine's completion signal for the background assets build; a missing field (engine layout drift) degrades to skipping the wait with a one-time warning instead of failing teardown.")]
    private static void WaitForAssetsBuildToSettle(ServerMain server)
    {
        try
        {
            FieldInfo? boxField = typeof(ServerMain).GetField(
                "serverAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance);
            object? box = boxField?.GetValue(server);
            FieldInfo? packetField = box?.GetType().GetField(
                "packet", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo? lengthField = box?.GetType().GetField(
                "Length", BindingFlags.Public | BindingFlags.Instance);
            if (box == null || packetField == null || lengthField == null)
            {
                WarnAssetsBuildProbeMissingOnce();
                return;
            }

            bool Built() => packetField.GetValue(box) != null || (int)lengthField.GetValue(box)! != 0;
            if (!AssetsBuildSettle.Wait(Built, AssetsBuildSettleTimeout, TimeSpan.FromMilliseconds(50)))
            {
                Console.Error.WriteLine(
                    "[Atlas] the boot's background server-assets build did not settle within " +
                    $"{AssetsBuildSettleTimeout.TotalSeconds:0.#}s; disposing the server anyway. If it is " +
                    "still in flight, its NRE on the statics Dispose nulls may crash the test process.");
            }
        }
        catch (Exception ex)
        {
            // Never let the guard itself break teardown: skipping the wait merely reverts to the
            // pre-fix behavior for this one dispose.
            Console.Error.WriteLine(
                $"[Atlas] could not wait for the server-assets build before dispose: {ex.GetType().Name}: " +
                ex.Message.ReplaceLineEndings(" "));
        }
    }

    /// <summary>Logs the engine-layout-drift warning for the assets-build wait once per process:
    /// hosts recycle many times per suite and the drift is a per-game-version fact, not a
    /// per-teardown one.</summary>
    private static void WarnAssetsBuildProbeMissingOnce()
    {
        if (Interlocked.Exchange(ref assetsBuildProbeWarned, 1) == 0)
        {
            Console.Error.WriteLine(
                "[Atlas] engine field 'ServerMain.serverAssetsPacket' (or its packet/Length shape) " +
                $"not found on game version {GameVersion.ShortGameVersion}; skipping the " +
                "dispose-time wait for the background assets build. A host disposed during that " +
                "build may crash the test process (see the fix for the boot assets-build race).");
        }
    }

    /// <summary>Points the engine's process-wide statics at this host's scratch data path and
    /// fresh logger. Kept static so the write-to-static-state is explicit at the call site: these
    /// ARE engine globals, one live server per process is the invariant that makes this safe.</summary>
    /// <param name="dataPath">The scratch data path for this host.</param>
    /// <param name="progArgs">The server arguments the logger is built from.</param>
    /// <remarks>Runs on the game thread.</remarks>
    private static void ConfigureEngineStatics(string dataPath, ServerProgramArgs progArgs)
    {
        GamePaths.DataPath = dataPath;
        GamePaths.EnsurePathsExist();
        ServerMain.Logger = new ServerLogger(progArgs);
        Lang.PreLoad(ServerMain.Logger, GamePaths.AssetsPath, "en");
    }

    /// <summary>Boots the embedded server: paths, logger, language, and launch.</summary>
    /// <param name="staging">The staging directory containing the mod-under-test.</param>
    /// <param name="bridgeStaging">The folder containing the staged copy of AtlasBridge.dll, alone.</param>
    /// <returns>The launched <see cref="ServerMain"/> instance.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    private ServerMain BootServer(string staging, string bridgeStaging)
    {
        var progArgs = new ServerProgramArgs
        {
            DataPath = _dataPath,
            AddModPath = new[] { staging, bridgeStaging },
        };

        ConfigureEngineStatics(_dataPath, progArgs);

        var startArgs = new StartServerArgs
        {
            Seed = _options.Seed,
            WorldName = _options.WorldName,
            SaveFileLocation = Path.Combine(_dataPath, "Saves", DataSeeder.WorldSaveFileName),
            AllowCreativeMode = true,
            PlayStyle = _options.PlayStyle,
            PlayStyleLangCode = _options.PlayStyle,
            WorldType = _options.WorldType,
            WorldConfiguration = JsonObject.FromJson("{}"),
            Language = "en",
            IsNew = true,
        };

        var server = new ServerMain(startArgs, new[] { "--dataPath", _dataPath }, progArgs, isDedicatedServer: false)
        {
            exitState = new GameExitState(),
        };
        server.PreLaunch();
        server.Launch();
        return server;
    }
}
