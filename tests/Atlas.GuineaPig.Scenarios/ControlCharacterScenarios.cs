using Atlas.XUnit;
using Xunit;

namespace Atlas.GuineaPig.Scenarios;

/// <summary>A scenario that fails on purpose with a message holding characters XML 1.0 cannot
/// carry: a control character (a protobuf payload printed raw is how it happens for real), a lone
/// surrogate and U+FFFE. <c>atlas run --parallel --trx</c> must still write a valid report for
/// it and exit with the run's own failure code.</summary>
public class ControlCharacterScenarios : AtlasScenarioBase
{
    /// <summary>The marker the tests look for, around the forbidden characters.</summary>
    public const string Marker = "payload bytes";

    [AtlasScenario]
    public Task Scenario_Should_FailWithXmlForbiddenCharacters_When_TheMessageHoldsThem()
    {
        Assert.Fail($"{Marker}: \u0012 lone surrogate: \ud800 noncharacter: ￾ end");
        return Task.CompletedTask;
    }
}
