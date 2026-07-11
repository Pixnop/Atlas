using System.Diagnostics;
using Atlas.Api;
using Atlas.Internal.Hosting;
using Atlas.Internal.Rollback;
using Atlas.Internal.Staging;

namespace Atlas.XUnit.Internal;

/// <summary>Owns the single live <see cref="ServerHost"/> for the process, scoped to one scenario
/// class at a time. Atlas test assemblies disable parallelization (see
/// <c>[assembly: CollectionBehavior(DisableTestParallelization = true)]</c>), so scenario classes run
/// sequentially; this registry enforces that assumption rather than relying on it silently.</summary>
internal static class HostRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, string> DeadClasses = new();

    private static Type? _ownerClass;
    private static ServerHost? _host;
    private static bool _busy;

    static HostRegistry() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeCurrentBestEffort();

    /// <summary>Gets or creates the live host for <paramref name="testClass"/>. If another class
    /// currently owns the host, the previous host is disposed first and a new one is booted from
    /// that class's <see cref="AtlasWorldAttribute"/>, <see cref="AtlasDataFilesAttribute"/> and
    /// assembly-level <see cref="AtlasModsAttribute"/> metadata.</summary>
    /// <param name="testClass">The scenario class requesting a host.</param>
    /// <returns>The live host, ready to run work on the game thread.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a second host is requested while another
    /// request is still in flight (concurrent scenario classes are not supported).</exception>
    /// <exception cref="ServerCrashedException">Thrown when <paramref name="testClass"/> was
    /// previously marked dead by <see cref="MarkDead"/> (a prior scenario crashed the host or was
    /// abandoned after a watchdog timeout); no new host is booted for it.</exception>
    public static async Task<ServerHost> GetOrCreateAsync(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        EnterExclusive();
        try
        {
            ThrowIfDead(testClass);
            if (_host != null && _ownerClass == testClass)
            {
                return _host;
            }

            EmitIsolationSummaryOfCurrentOwner();
            await DisposeCurrentAsync().ConfigureAwait(false);
            return await CreateAsync(testClass).ConfigureAwait(false);
        }
        finally
        {
            ExitExclusive();
        }
    }

    /// <summary>Disposes the current host for <paramref name="testClass"/> and boots a fresh one from
    /// the same metadata, giving the caller an empty world.</summary>
    /// <param name="testClass">The scenario class requesting a fresh world.</param>
    /// <returns>The newly booted host.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a second host is requested while another
    /// request is still in flight.</exception>
    /// <exception cref="ServerCrashedException">Thrown when <paramref name="testClass"/> was
    /// previously marked dead by <see cref="MarkDead"/>.</exception>
    public static async Task<ServerHost> RecycleAsync(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        EnterExclusive();
        try
        {
            ThrowIfDead(testClass);
            if (_ownerClass != testClass)
            {
                // A FreshWorld scenario can be the first of its class while another class still
                // owns the host: that hand-off ends the previous class, so its summary is due.
                EmitIsolationSummaryOfCurrentOwner();
            }

            await DisposeCurrentAsync().ConfigureAwait(false);
            return await CreateAsync(testClass).ConfigureAwait(false);
        }
        finally
        {
            ExitExclusive();
        }
    }

    /// <summary>Gives <paramref name="testClass"/> a host whose world is in its snapshot state:
    /// gets or creates the class host, then rolls its world back (capturing the snapshot first if
    /// this is the host's first rollback request). Fail closed: when capture or restore fails,
    /// <see cref="Atlas.Internal.Hosting.ServerHost.TryRollbackWorldAsync"/> has already logged a
    /// warning and this method degrades to <see cref="RecycleAsync"/>, so the scenario still gets
    /// a clean world, just at full recycle cost. Every outcome is recorded in the
    /// <see cref="IsolationLedger"/>, and a degraded outcome carries the classified reason, the
    /// one-line detail and the measured recycle cost, so the invoker can attach them to the
    /// scenario's test output (or fail the scenario under strict isolation).</summary>
    /// <param name="testClass">The scenario class requesting rollback isolation.</param>
    /// <returns>The outcome: the host (its world in the snapshot state, rolled back or freshly
    /// booted) plus the degrade evidence, if any.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the class has joined test players (stage 1
    /// rollback does not roll player state back; this authoring error is not papered over by the
    /// fallback) or when a second host is requested while another request is still in flight.</exception>
    /// <exception cref="ServerCrashedException">Thrown when <paramref name="testClass"/> was
    /// previously marked dead by <see cref="MarkDead"/>, or when the host crashed.</exception>
    public static async Task<RollbackOutcome> RollbackOrRecycleAsync(Type testClass)
    {
        ServerHost host = await GetOrCreateAsync(testClass).ConfigureAwait(false);
        RollbackAttempt attempt = await host.TryRollbackWorldAsync().ConfigureAwait(false);
        if (attempt.Succeeded)
        {
            IsolationLedger.RecordRollback(testClass);
            return RollbackOutcome.RolledBack(host);
        }

        IsolationLedger.RecordDegrade(testClass, attempt.DegradeReason);
        var recycleWatch = Stopwatch.StartNew();
        ServerHost replacement = await RecycleAsync(testClass).ConfigureAwait(false);
        recycleWatch.Stop();
        return RollbackOutcome.DegradedToRecycle(
            replacement, attempt.DegradeReason, attempt.DegradeDetail!, recycleWatch.Elapsed);
    }

    /// <summary>Disposes the current host gracefully (the engine's shutdown persists its world
    /// into the host's scratch save) and returns the full path of that save file, or
    /// <see langword="null"/> when no host is live. This is the harvest seam of `atlas fixture`:
    /// after the builder scenario passed, the CLI invokes this method BY NAME through reflection
    /// (it deliberately references neither Atlas nor Atlas.XUnit), so the type name, method name
    /// and signature are load-bearing; keep <c>Atlas.Cli.FixtureHarvest</c> in sync when
    /// changing any of them.</summary>
    /// <returns>The disposed host's save file path, or <see langword="null"/> when no host was
    /// live. The file itself is only guaranteed to exist after a graceful teardown; callers must
    /// check.</returns>
    public static async Task<string?> ShutDownAndHarvestSavePathAsync()
    {
        EnterExclusive();
        try
        {
            string? dataPath = _host?.DataPath;
            EmitIsolationSummaryOfCurrentOwner();
            await DisposeCurrentAsync().ConfigureAwait(false);
            return dataPath is null
                ? null
                : Path.Combine(dataPath, "Saves", DataSeeder.WorldSaveFileName);
        }
        finally
        {
            ExitExclusive();
        }
    }

    /// <summary>Marks <paramref name="testClass"/>'s host as dead: every later scenario of that class
    /// fails immediately with <paramref name="message"/> instead of trying to reuse or recreate the
    /// host.</summary>
    /// <param name="testClass">The scenario class whose host is no longer trustworthy.</param>
    /// <param name="message">The failure message reported to later scenarios of the class.</param>
    /// <remarks>Used both when the embedded server crashes outright and when a scenario's watchdog
    /// times out: in the timeout case the game thread may still be running the abandoned scenario, so
    /// there is no safe way to keep using that host even though it has not technically crashed.</remarks>
    public static void MarkDead(Type testClass, string message)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        ArgumentException.ThrowIfNullOrEmpty(message);
        lock (Gate)
        {
            DeadClasses[testClass] = message;
        }
    }

    private static void ThrowIfDead(Type testClass)
    {
        string? message;
        lock (Gate)
        {
            DeadClasses.TryGetValue(testClass, out message);
        }

        if (message != null)
        {
            throw new ServerCrashedException(message, new InvalidOperationException(message));
        }
    }

    private static void EnterExclusive()
    {
        lock (Gate)
        {
            if (_busy)
            {
                throw new AtlasSetupException(
                    "Two Atlas scenario hosts were requested concurrently. Atlas test assemblies must " +
                    "disable parallelization: add [assembly: CollectionBehavior(DisableTestParallelization = true)].");
            }

            _busy = true;
        }
    }

    private static void ExitExclusive()
    {
        lock (Gate)
        {
            _busy = false;
        }
    }

    private static async Task<ServerHost> CreateAsync(Type testClass)
    {
        AtlasHostRecipe recipe = AttributeMapper.Map(testClass);
        var host = new ServerHost(recipe.Options, recipe.ModPaths, recipe.ModBaseDir, recipe.DataFiles);
        await host.StartAsync().ConfigureAwait(false);
        _host = host;
        _ownerClass = testClass;
        return host;
    }

    private static async Task DisposeCurrentAsync()
    {
        if (_host == null)
        {
            return;
        }

        ServerHost previous = _host;
        _host = null;
        _ownerClass = null;
        await previous.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Prints the isolation summary of the class currently owning the host, if it has
    /// one worth printing (see <see cref="IsolationLedger.DrainSummary"/>). Called at every
    /// point a class hands its host off: owner change, the fixture harvest and process exit,
    /// which together cover "end of class" for every class that owned a host.</summary>
    private static void EmitIsolationSummaryOfCurrentOwner()
    {
        if (_ownerClass is { } owner && IsolationLedger.DrainSummary(owner) is { } summary)
        {
            Console.Error.WriteLine(summary);
        }
    }

    private static void DisposeCurrentBestEffort()
    {
        try
        {
            EmitIsolationSummaryOfCurrentOwner();
            DisposeCurrentAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort: the process is exiting, nothing left to report to.
        }
    }
}
