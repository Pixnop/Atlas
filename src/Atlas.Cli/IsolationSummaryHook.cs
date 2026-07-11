using System.Reflection;

namespace Atlas.Cli;

/// <summary>Bridges worker mode to the harness's isolation summary sink,
/// <c>Atlas.XUnit.Internal.IsolationSummarySink.Install</c>: once installed, every per-class
/// isolation summary the harness prints to stderr at a host hand-off is also delivered to the
/// worker, which turns it into a <c>class-summary</c> protocol event. The CLI deliberately
/// references neither Atlas nor Atlas.XUnit (the scenario assembly ships the whole harness),
/// so the sink is installed BY NAME through reflection, mirroring <see cref="FixtureHarvest"/>;
/// the names below are the contract both sides pin. Because Atlas.XUnit only loads once the
/// scenario assembly does, registration watches <see cref="AppDomain.AssemblyLoad"/> and
/// installs the handler the moment the harness appears (or immediately when it is already
/// loaded). A scenario assembly built against an older harness simply never gets the sink
/// installed: summaries stay stderr-only, exactly the pre-#66 behavior.</summary>
internal static class IsolationSummaryHook
{
    /// <summary>Fully qualified name of the harness's summary sink type.</summary>
    internal const string SinkTypeName = "Atlas.XUnit.Internal.IsolationSummarySink";

    /// <summary>Name of the install method (static, takes one nullable Action&lt;string, string&gt;).</summary>
    internal const string InstallMethodName = "Install";

    /// <summary>Registers <paramref name="handler"/> as the harness's isolation summary sink,
    /// now if the harness is already loaded or as soon as it loads. Dispose the registration to
    /// uninstall the handler and stop watching.</summary>
    /// <param name="handler">Receives the class's fully qualified name and the formatted
    /// summary line, on whatever thread the harness hands the host off.</param>
    /// <returns>The registration to dispose when the run is over.</returns>
    public static IDisposable Register(Action<string, string> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new Registration(handler);
    }

    /// <summary>Locates the sink's install method on the loaded harness, if any. Separated from
    /// the registration so the lookup is testable against arbitrary assembly lists.</summary>
    /// <param name="loadedAssemblies">The assemblies to search.</param>
    /// <returns>The install method, or <see langword="null"/> when the harness is not among
    /// <paramref name="loadedAssemblies"/> or predates the sink.</returns>
    internal static MethodInfo? FindInstallMethod(IEnumerable<Assembly> loadedAssemblies) =>
        loadedAssemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == FixtureHarvest.AdapterAssemblyName)?
            .GetType(SinkTypeName)?
            .GetMethod(InstallMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>One live registration: installs the handler at (or after) harness load,
    /// uninstalls it on dispose. The lock serializes the install-once decision between the
    /// initial scan and concurrent assembly-load callbacks.</summary>
    private sealed class Registration : IDisposable
    {
        private readonly object _gate = new();
        private readonly Action<string, string> _handler;
        private MethodInfo? _installedThrough;
        private bool _disposed;

        public Registration(Action<string, string> handler)
        {
            _handler = handler;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            TryInstall(AppDomain.CurrentDomain.GetAssemblies());
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                _installedThrough?.Invoke(null, [null]);
            }
        }

        private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
        {
            if (args.LoadedAssembly.GetName().Name == FixtureHarvest.AdapterAssemblyName)
            {
                TryInstall([args.LoadedAssembly]);
            }
        }

        private void TryInstall(IEnumerable<Assembly> assemblies)
        {
            lock (_gate)
            {
                if (_disposed || _installedThrough is not null)
                {
                    return;
                }

                if (FindInstallMethod(assemblies) is { } install)
                {
                    install.Invoke(null, [_handler]);
                    _installedThrough = install;
                }
            }
        }
    }
}
