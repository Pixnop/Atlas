using System.Reflection;
using Atlas.Cli;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.Cli;

/// <summary>Covers both halves of the summary bridge: the CLI-side reflection hook
/// (<see cref="IsolationSummaryHook"/>) and the harness-side sink
/// (<see cref="IsolationSummarySink"/>) it installs into. The sink is process-wide static
/// state, so every test that touches it lives in this one class (xunit runs the tests of a
/// class sequentially) and uninstalls on its way out.</summary>
public class IsolationSummaryHookTests
{
    [Fact]
    public void FindInstallMethod_Should_LocateTheSink_When_TheHarnessIsLoaded()
    {
        MethodInfo? install = IsolationSummaryHook.FindInstallMethod([typeof(IsolationSummarySink).Assembly]);

        Assert.NotNull(install);
        Assert.Equal(IsolationSummaryHook.InstallMethodName, install.Name);
        Assert.True(install.IsStatic);
    }

    [Fact]
    public void FindInstallMethod_Should_ReturnNull_When_TheHarnessIsNotAmongTheAssemblies()
    {
        Assert.Null(IsolationSummaryHook.FindInstallMethod([typeof(IsolationSummaryHookTests).Assembly]));
        Assert.Null(IsolationSummaryHook.FindInstallMethod([]));
    }

    [Fact]
    public void Register_Should_DeliverPublishedSummaries_When_TheHarnessIsAlreadyLoaded()
    {
        // Atlas.XUnit is a direct reference of this test project, so the registration's initial
        // scan of the loaded assemblies must install the handler immediately, no load event
        // needed: the exact situation of a worker whose scenario assembly loaded the harness
        // before the sink was registered.
        var received = new List<(string ClassName, string Summary)>();
        using (IsolationSummaryHook.Register((className, summary) => received.Add((className, summary))))
        {
            IsolationSummarySink.Publish("Ns.A", "[Atlas] isolation summary for Ns.A: 1 restart(s) (7.1 s total).");
        }

        (string className, string summary) = Assert.Single(received);
        Assert.Equal("Ns.A", className);
        Assert.Contains("1 restart(s)", summary);
    }

    [Fact]
    public void Dispose_Should_UninstallTheHandler_When_TheRegistrationEnds()
    {
        var received = new List<string>();
        IDisposable registration = IsolationSummaryHook.Register((_, summary) => received.Add(summary));
        registration.Dispose();

        IsolationSummarySink.Publish("Ns.A", "too late");

        Assert.Empty(received);
        registration.Dispose(); // idempotent: a double dispose must not throw
    }

    [Fact]
    public void Publish_Should_BeANoOp_When_NoHandlerIsInstalled()
    {
        IsolationSummarySink.Install(null);

        Assert.Null(Record.Exception(() => IsolationSummarySink.Publish("Ns.A", "nobody listens")));
    }

    [Fact]
    public void Publish_Should_SwallowHandlerFailures_When_TheHandlerThrows()
    {
        try
        {
            IsolationSummarySink.Install((_, _) => throw new InvalidOperationException("broken sink"));

            // A reporting failure must never fail the scenario whose host hand-off published.
            Assert.Null(Record.Exception(() => IsolationSummarySink.Publish("Ns.A", "boom")));
        }
        finally
        {
            IsolationSummarySink.Install(null);
        }
    }
}
