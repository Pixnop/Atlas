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

    private static Type? _ownerClass;
    private static ServerHost? _host;
    private static bool _busy;

    static HostRegistry() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeCurrentBestEffort();

    /// <summary>Gets or creates the live host for <paramref name="testClass"/>. If another class
    /// currently owns the host, the previous host is disposed first and a new one is booted from
    /// that class's <see cref="AtlasWorldAttribute"/> and assembly-level <see cref="AtlasModsAttribute"/>
    /// metadata.</summary>
    /// <param name="testClass">The scenario class requesting a host.</param>
    /// <returns>The live host, ready to run work on the game thread.</returns>
    /// <exception cref="AtlasSetupException">Thrown when a second host is requested while another
    /// request is still in flight (concurrent scenario classes are not supported).</exception>
    public static async Task<ServerHost> GetOrCreateAsync(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        EnterExclusive();
        try
        {
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
    public static async Task<ServerHost> RecycleAsync(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        EnterExclusive();
        try
        {
            await DisposeCurrentAsync().ConfigureAwait(false);
            return await CreateAsync(testClass).ConfigureAwait(false);
        }
        finally
        {
            ExitExclusive();
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
        var host = new ServerHost(recipe.Options, recipe.ModPaths, recipe.ModBaseDir);
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
