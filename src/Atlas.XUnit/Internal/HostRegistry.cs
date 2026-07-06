using Atlas.Api;
using Atlas.Internal.Hosting;

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
    /// a clean world, just at full recycle cost.</summary>
    /// <param name="testClass">The scenario class requesting rollback isolation.</param>
    /// <returns>The host, its world in the snapshot state (rolled back or freshly booted).</returns>
    /// <exception cref="AtlasSetupException">Thrown when the class has joined test players (stage 1
    /// rollback does not roll player state back; this authoring error is not papered over by the
    /// fallback) or when a second host is requested while another request is still in flight.</exception>
    /// <exception cref="ServerCrashedException">Thrown when <paramref name="testClass"/> was
    /// previously marked dead by <see cref="MarkDead"/>, or when the host crashed.</exception>
    public static async Task<ServerHost> RollbackOrRecycleAsync(Type testClass)
    {
        ServerHost host = await GetOrCreateAsync(testClass).ConfigureAwait(false);
        if (await host.TryRollbackWorldAsync().ConfigureAwait(false))
        {
            return host;
        }

        return await RecycleAsync(testClass).ConfigureAwait(false);
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

    private static void DisposeCurrentBestEffort()
    {
        try
        {
            DisposeCurrentAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort: the process is exiting, nothing left to report to.
        }
    }
}
