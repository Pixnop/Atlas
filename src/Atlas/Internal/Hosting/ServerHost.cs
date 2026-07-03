using Atlas.Api;
using Atlas.Internal.Bootstrap;
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
    private readonly WorldOptions _options;
    private readonly IReadOnlyList<string> _modPaths;
    private readonly string _modBaseDir;
    private readonly string _dataPath = Path.Combine(
        Path.GetTempPath(), "atlas", Guid.NewGuid().ToString("N"));

    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stop = new();

    private Thread? _gameThread;
    private GameThreadScheduler? _scheduler;
    private TickSource? _ticks;
    private ICoreServerAPI? _api;
    private volatile Exception? _crash;

    /// <summary>Initializes a new instance of the <see cref="ServerHost"/> class.</summary>
    /// <param name="options">World configuration for the embedded server.</param>
    /// <param name="modPaths">Paths to mods-under-test to stage alongside the bridge.</param>
    /// <param name="modBaseDir">Base directory used to resolve relative mod paths.</param>
    public ServerHost(WorldOptions options, IReadOnlyList<string> modPaths, string modBaseDir)
    {
        _options = options;
        _modPaths = modPaths;
        _modBaseDir = modBaseDir;
    }

    /// <summary>Gets the number of ticks raised so far, or zero before the host is ready.</summary>
    /// <remarks>Safe to read from any thread: backed by <see cref="TickSource.TickCount"/>, which uses
    /// a volatile read. Intended for diagnostics (e.g. a watchdog timeout message) where a value that
    /// is stale by a tick or two is acceptable.</remarks>
    public int CurrentTick => _ticks?.TickCount ?? 0;

    /// <summary>Gets the crash captured by the game thread, if the embedded server died.</summary>
    /// <remarks>Belt-and-suspenders for callers (e.g. the xUnit invoker) that observe a different
    /// symptom of a crash, such as a watchdog timeout, and want to recover the true root cause.</remarks>
    internal Exception? CrashException => _crash;

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
        => RunOnGameThreadAsync((api, ticks) => scenario(new WorldSession(api, ticks)));

    /// <summary>Stops and disposes the embedded server, then joins the game thread.</summary>
    /// <returns>A task that completes when the game thread has exited, or when the bounded join
    /// times out waiting for a wedged game thread.</returns>
    /// <remarks>A normal shutdown observes <see cref="_stop"/> at the top of the pump loop in
    /// <see cref="GameThreadMain"/> and joins in roughly 1-2 seconds. The 30 second bound only
    /// matters when the game thread is wedged inside a single <c>server.Process()</c> call (the
    /// same scenario the scenario watchdog guards against) and never observes the cancellation. In
    /// that case the join gives up and this method returns without throwing: the thread is created
    /// with <c>IsBackground = true</c>, so abandoning it does not block process exit, and a wedged
    /// embedded server cannot be shut down safely from the outside anyway.</remarks>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_gameThread != null)
        {
            await Task.Run(() => _gameThread.Join(TimeSpan.FromSeconds(30))).ConfigureAwait(false);
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
    private void GameThreadMain()
    {
        ServerMain? server = null;
        try
        {
            string install = VsInstall.Locate();
            GameEnvironment.Initialize(install);
            Directory.SetCurrentDirectory(install);

            string staging = Path.Combine(_dataPath, "TestMods");

            // Stage the mod-under-test.
            ModStager.Stage(_modPaths, _modBaseDir, staging);

            // Stage AtlasBridge.dll alone into its own folder. It must NOT share a folder with
            // the consumer test project's bin output: that directory is full of non-mod dlls
            // (test framework, mocking libraries, etc.) that the game's ModLoader would also
            // scan. The ModLoader therefore loads a COPY of AtlasBridge.dll, distinct from the
            // engine's own assembly instance, so BridgeRendezvous.Reset() wires up an
            // AppDomain-slot handoff instead of relying on shared statics.
            string bridgeStaging = Path.Combine(_dataPath, "BridgeMod");
            Directory.CreateDirectory(bridgeStaging);
            string bridgeSource = typeof(Bridge.BridgeRendezvous).Assembly.Location;
            File.Copy(bridgeSource, Path.Combine(bridgeStaging, Path.GetFileName(bridgeSource)), overwrite: true);

            Bridge.BridgeRendezvous.Reset();

            _scheduler = GameThreadScheduler.InstallOnCurrentThread();
            _ticks = new TickSource();
            Bridge.BridgeRendezvous.TickFired += _ticks.RaiseTick;

            server = BootServer(install, staging, bridgeStaging);

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
                // Swallow: shutdown is best-effort after a crash. Keep the original exception
                // as the primary cause and only aggregate if Stop itself throws.
                _crash = new AggregateException(ex, stopEx);
            }

            // A scenario may be parked on await World.Ticks(...) (or another TickSource wait)
            // right now, with no more ticks ever coming: the game thread is about to exit. Fault
            // every pending waiter with the true cause (wrapped the same way ThrowIfCrashed wraps
            // it) and drain the scheduler so the scenario task observes it promptly, instead of
            // hanging until the watchdog fires a ScenarioTimeoutException that points away from
            // the real cause. Both may be unset if the crash happened before they were assigned.
            ServerCrashedException crashException = WrapCrash(_crash);
            _ticks?.FailAll(crashException);
            _scheduler?.DrainPending();
        }
        finally
        {
            try
            {
                server?.Dispose();
            }
            catch (NullReferenceException)
            {
                // Swallow: Vintage Story 1.22.2 can throw NullReferenceException during
                // ServerSystemMonitor.Dispose() on shutdown. This is a known flake in the
                // embedded server, not a failure of our shutdown logic.
            }
        }
    }

    /// <summary>Boots the embedded server: paths, logger, language, and launch.</summary>
    /// <param name="install">The Vintage Story installation directory.</param>
    /// <param name="staging">The staging directory containing the mod-under-test.</param>
    /// <param name="bridgeStaging">The folder containing the staged copy of AtlasBridge.dll, alone.</param>
    /// <returns>The launched <see cref="ServerMain"/> instance.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    private ServerMain BootServer(string install, string staging, string bridgeStaging)
    {
        GamePaths.DataPath = _dataPath;
        GamePaths.EnsurePathsExist();

        var progArgs = new ServerProgramArgs
        {
            DataPath = _dataPath,
            AddModPath = new[] { staging, bridgeStaging },
        };

        ServerMain.Logger = new ServerLogger(progArgs);
        Lang.PreLoad(ServerMain.Logger, GamePaths.AssetsPath, "en");

        var startArgs = new StartServerArgs
        {
            Seed = _options.Seed,
            WorldName = _options.WorldName,
            SaveFileLocation = Path.Combine(_dataPath, "Saves", "atlas.vcdbs"),
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
