using System.Diagnostics.CodeAnalysis;
using Atlas.Engine.Tests.Support;
using Atlas.XUnit;
using Xunit.Abstractions;

namespace Atlas.Engine.Tests;

/// <summary>Covers the server log report a failing scenario carries (issue #186): when the cause
/// of a failure is something a mod logged at boot, the scenario's own output says where the
/// engine's log is and lists the Error entries since the boot, and a passing scenario says
/// nothing. Each test drives a real <c>AtlasTestCase</c> through the full pipeline against a
/// private probe class staging BootDiagnosticsFixtureMod, which logs two Error entries at boot
/// (a malformed blocktype and a wrong-typed property) without failing it, the same technique as
/// <see cref="IsolationObservabilityTests"/>.</summary>
[Trait("Category", "E2E")]
public class ServerLogOnFailureTests
{
    private const string ServerLogLine = "[Atlas] server log: ";

    /// <summary>Why the probe bodies below carry no assertion of their own.</summary>
    private const string ProbeJustification =
        "Probe scenario driven by one of the tests above, which asserts what the pipeline reported " +
        "for it, not the body.";

    [Fact]
    public async Task FailedScenario_Should_NameTheServerLogAndListTheEngineErrors_When_AModLoggedErrorsAtBoot()
    {
        IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
            typeof(LoggingModProbeScenarios),
            nameof(LoggingModProbeScenarios.Scenario_Should_Fail),
            strictIsolation: false,
            freshWorld: true);

        ITestFailed failed = Assert.Single(messages.OfType<ITestFailed>());
        Assert.Contains("probe failure", Assert.Single(failed.Messages), StringComparison.Ordinal);
        Assert.Contains("error(s) logged by the engine since the boot:", failed.Output, StringComparison.Ordinal);
        Assert.Contains("bootdiagfixture:blocktypes/malformed.json", failed.Output, StringComparison.Ordinal);
        Assert.Contains("bootdiagfixture:bootdiagbadproperty", failed.Output, StringComparison.Ordinal);

        // The warnings the fixture logs are in the diagnostics, not in this report.
        Assert.DoesNotContain("used its own logger", failed.Output, StringComparison.Ordinal);

        // The path it names is the real log, kept because the class failed.
        string logPath = ServerLogPath(failed.Output);
        Assert.EndsWith(Path.Combine("Logs", "server-main.log"), logPath, StringComparison.Ordinal);
        Assert.True(File.Exists(logPath), $"the report names '{logPath}', which does not exist");

        // The same report is streamed as a live output message.
        Assert.Contains(
            messages.OfType<ITestOutput>(),
            output => output.Output.Contains(ServerLogLine, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PassedScenario_Should_SayNothingAboutTheServerLog_When_ItsHostLoggedErrorsAtBoot()
    {
        IReadOnlyList<IMessageSinkMessage> messages = await ProbeCases.RunAsync(
            typeof(LoggingModProbeScenarios),
            nameof(LoggingModProbeScenarios.Scenario_Should_Pass),
            strictIsolation: false,
            freshWorld: true);

        ITestPassed passed = Assert.Single(messages.OfType<ITestPassed>());
        Assert.DoesNotContain(ServerLogLine, passed.Output, StringComparison.Ordinal);
        Assert.Empty(messages.OfType<ITestOutput>());
    }

    private static string ServerLogPath(string output)
    {
        int start = output.IndexOf(ServerLogLine, StringComparison.Ordinal) + ServerLogLine.Length;
        int end = output.IndexOf('\n', start);
        return output[start..(end < 0 ? output.Length : end)].TrimEnd('\r');
    }

#pragma warning disable xUnit1000 // Probe classes are driven through the pipeline by the tests above.

    /// <summary>Probe staging a mod that logs Error entries at boot.</summary>
    [AtlasWorld(Mods = ["../../../../BootDiagnosticsFixtureMod"])]
    private sealed class LoggingModProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_Fail() => throw new InvalidOperationException("probe failure");

        [AtlasScenario(FreshWorld = true)]
        [SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = ProbeJustification)]
        public Task Scenario_Should_Pass() => Task.CompletedTask;
    }

#pragma warning restore xUnit1000
}
