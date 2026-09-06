using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class CliParseResultTests
{
    // Not a [Theory]: CliParseResult is internal, so it cannot appear in a public test
    // signature, and MemberData would have to smuggle it through object[]. A property rather
    // than a static readonly field, so the factory calls are attributed to the test that reads
    // them: a field initializer runs once, under whichever test happens to be first, and Stryker
    // then reruns only that one for every mutant in these five factories.
    private static (string Factory, CliParseResult Result)[] Understood =>
    [
        ("ForRun", CliParseResult.ForRun(new RunArguments("S.dll", null, List: false))),
        ("ForFixture", CliParseResult.ForFixture(new FixtureArguments("S.dll", "Builder", "w.vcdbs"))),
        ("ForDiff", CliParseResult.ForDiff(new DiffArguments("a.trx", "b.trx"))),
        ("ForStage", CliParseResult.ForStage(new StageArguments("bin/Release"))),
        ("Failure", CliParseResult.Failure("unknown command")),
    ];

    [Fact]
    public void Factories_Should_FillOneSlotAndAskForNeitherHelpNorVersion_When_Called()
    {
        // Program dispatches on ShowHelp and ShowVersion before it looks at anything else, so a
        // factory that set either would print usage and exit 0 instead of running the command,
        // or instead of reporting the usage error. The parser tests reach these factories
        // through Parse and only ever assert the slot, never the two flags.
        foreach ((string factory, CliParseResult result) in Understood)
        {
            Assert.False(result.ShowHelp, $"{factory} must not ask for help");
            Assert.False(result.ShowVersion, $"{factory} must not ask for the version");

            object?[] slots = [result.Run, result.Fixture, result.Diff, result.Stage, result.Error];
            Assert.Single(slots, slot => slot is not null);
        }
    }

    [Fact]
    public void HelpAndVersion_Should_FillNoSlot_When_TheShellOnlyHasToPrint()
    {
        Assert.True(CliParseResult.Help.ShowHelp);
        Assert.False(CliParseResult.Help.ShowVersion);
        Assert.True(CliParseResult.Version.ShowVersion);
        Assert.False(CliParseResult.Version.ShowHelp);

        foreach (CliParseResult result in new[] { CliParseResult.Help, CliParseResult.Version })
        {
            Assert.Null(result.Run);
            Assert.Null(result.Fixture);
            Assert.Null(result.Diff);
            Assert.Null(result.Stage);
            Assert.Null(result.Error);
        }
    }
}
