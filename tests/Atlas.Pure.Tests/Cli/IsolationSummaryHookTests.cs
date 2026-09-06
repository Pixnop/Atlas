using Atlas.Cli;
using Atlas.XUnit.Internal;

namespace Atlas.Pure.Tests.Cli;

/// <summary>Covers both halves of the summary bridge: the CLI-side hook
/// (<see cref="IsolationSummaryHook"/>) and the harness-side sink
/// (<see cref="IsolationSummarySink"/>) it installs into. The sink is process-wide static
/// state, so every test that touches it lives in this one class (xunit runs the tests of a
/// class sequentially) and uninstalls on its way out.</summary>
public class IsolationSummaryHookTests
{
    [Fact]
    public void Register_Should_DeliverPublishedSummaries_When_TheHandlerIsInstalled()
    {
        var received = new List<(string ClassName, string Summary)>();
        using (IsolationSummaryHook.Register((className, summary) => received.Add((className, summary)), out string? error))
        {
            Assert.Null(error);
            IsolationSummarySink.Publish("Ns.A", "[Atlas] isolation summary for Ns.A: 1 restart(s) (7.1 s total).");
        }

        (string className, string summary) = Assert.Single(received);
        Assert.Equal("Ns.A", className);
        Assert.Contains("1 restart(s)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_Should_UninstallTheHandler_When_TheRegistrationEnds()
    {
        var received = new List<string>();
        IDisposable? registration = IsolationSummaryHook.Register((_, summary) => received.Add(summary), out _);
        Assert.NotNull(registration);
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
