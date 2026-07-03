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

    /// <summary>Stops and disposes the embedded server, then joins the game thread.</summary>
    /// <returns>A task that completes when the game thread has exited.</returns>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_gameThread != null)
        {
            await Task.Run(_gameThread.Join).ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private void ThrowIfCrashed()
    {
        if (_crash != null)
        {
            throw new ServerCrashedException($"Embedded server died; logs: {_dataPath}", _crash);
        }
    }

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

            // Stage only the mod-under-test here. AtlasBridge.dll is NOT copied into staging:
            // both the host process and the ModLoader must load it from the exact same path
            // for the rendezvous statics to be shared (LoadFrom caches by path; a copy would
            // be a distinct assembly instance with its own statics). Instead, the bridge's own
            // bin directory is added as a second mod path below (see BootServer).
            ModStager.Stage(_modPaths, _modBaseDir, staging);
            Bridge.BridgeRendezvous.Reset();

            _scheduler = GameThreadScheduler.InstallOnCurrentThread();
            _ticks = new TickSource();
            Bridge.BridgeRendezvous.TickFired += _ticks.RaiseTick;

            string bridgeDir = Path.GetDirectoryName(typeof(Bridge.BridgeRendezvous).Assembly.Location)!;
            server = BootServer(install, staging, bridgeDir);
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
        }
        finally
        {
            server?.Dispose();
        }
    }

    /// <summary>Boots the embedded server: paths, logger, language, and launch.</summary>
    /// <param name="install">The Vintage Story installation directory.</param>
    /// <param name="staging">The staging directory containing the mod-under-test.</param>
    /// <param name="bridgeDir">The bridge assembly's own bin directory.</param>
    /// <returns>The launched <see cref="ServerMain"/> instance.</returns>
    /// <remarks>Runs on the game thread.</remarks>
    private ServerMain BootServer(string install, string staging, string bridgeDir)
    {
        GamePaths.DataPath = _dataPath;
        GamePaths.EnsurePathsExist();

        var progArgs = new ServerProgramArgs
        {
            DataPath = _dataPath,
            AddModPath = new[] { staging, bridgeDir },
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
