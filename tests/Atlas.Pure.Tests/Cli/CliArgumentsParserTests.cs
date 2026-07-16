using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class CliArgumentsParserTests
{
    [Fact]
    public void Parse_Should_ShowHelp_When_NoArguments()
    {
        CliParseResult result = CliArgumentsParser.Parse([]);

        Assert.True(result.ShowHelp);
        Assert.Null(result.Arguments);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Parse_Should_ShowHelp_When_HelpTokenIsFirst(string token)
    {
        Assert.True(CliArgumentsParser.Parse([token]).ShowHelp);
    }

    [Fact]
    public void Parse_Should_ShowHelp_When_HelpTokenFollowsRunCommand()
    {
        Assert.True(CliArgumentsParser.Parse(["run", "--help"]).ShowHelp);
    }

    [Fact]
    public void Parse_Should_Fail_When_CommandIsUnknown()
    {
        CliParseResult result = CliArgumentsParser.Parse(["walk", "Scenarios.dll"]);

        Assert.NotNull(result.Error);
        Assert.Contains("walk", result.Error);
    }

    [Fact]
    public void Parse_Should_ReturnDefaults_When_OnlyAssemblyPathGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "bin/Scenarios.dll"]);

        Assert.Null(result.Error);
        Assert.False(result.ShowHelp);
        Assert.Equal(new CliArguments("bin/Scenarios.dll", null, false), result.Arguments);
    }

    [Fact]
    public void Parse_Should_CaptureFilterValue_When_FilterIsSeparateToken()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--filter", "Chest"]);

        Assert.Equal(new CliArguments("Scenarios.dll", "Chest", false), result.Arguments);
    }

    [Fact]
    public void Parse_Should_CaptureFilterValue_When_FilterUsesEqualsSyntax()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--filter=Chest"]);

        Assert.Equal(new CliArguments("Scenarios.dll", "Chest", false), result.Arguments);
    }

    [Fact]
    public void Parse_Should_SetList_When_ListFlagGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "--list", "Scenarios.dll"]);

        Assert.Equal(new CliArguments("Scenarios.dll", null, true), result.Arguments);
    }

    [Fact]
    public void Parse_Should_AcceptOptionsInAnyOrder_When_AllAreGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "--filter", "Transit", "Scenarios.dll", "--list"]);

        Assert.Equal(new CliArguments("Scenarios.dll", "Transit", true), result.Arguments);
    }

    [Fact]
    public void Parse_Should_Fail_When_FilterValueIsMissing()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--filter"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--filter", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_AssemblyPathIsMissing()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "--list"]);

        Assert.NotNull(result.Error);
        Assert.Contains("assembly", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_OptionIsUnknown()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--warp"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--warp", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_FlagOptionCarriesAnInlineValue()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--list=yes"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--list=yes", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_TwoAssemblyPathsGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "A.dll", "B.dll"]);

        Assert.NotNull(result.Error);
        Assert.Contains("B.dll", result.Error);
    }

    [Fact]
    public void Parse_Should_SetWorker_When_WorkerFlagGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--worker"]);

        Assert.Null(result.Error);
        Assert.True(result.Arguments!.Worker);
        Assert.Null(result.Arguments.Classes);
    }

    [Fact]
    public void Parse_Should_CombineWorkerAndList_When_BothFlagsGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--list", "--worker"]);

        Assert.True(result.Arguments!.Worker);
        Assert.True(result.Arguments.List);
    }

    [Fact]
    public void Parse_Should_SplitClasses_When_CommaSeparatedListGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--worker", "--classes", "Ns.A,Ns.B"]);

        Assert.Null(result.Error);
        Assert.Equal(["Ns.A", "Ns.B"], result.Arguments!.Classes);
    }

    [Fact]
    public void Parse_Should_SplitClasses_When_ClassesUsesEqualsSyntax()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--worker", "--classes=Ns.A,Ns.B"]);

        Assert.Equal(["Ns.A", "Ns.B"], result.Arguments!.Classes);
    }

    [Fact]
    public void Parse_Should_TrimAndDropEmptyEntries_When_ClassesListIsSloppy()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--worker", "--classes", " Ns.A , ,Ns.B, "]);

        Assert.Equal(["Ns.A", "Ns.B"], result.Arguments!.Classes);
    }

    [Fact]
    public void Parse_Should_Fail_When_ClassesValueIsMissing()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--worker", "--classes"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--classes", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ,")]
    public void Parse_Should_Fail_When_ClassesValueIsEmpty(string value)
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--worker", "--classes", value]);

        Assert.NotNull(result.Error);
        Assert.Contains("at least one", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_ClassesGivenWithoutWorker()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--classes", "Ns.A"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--classes requires --worker", result.Error);
    }

    [Fact]
    public void Parse_Should_AcceptWorkerAfterClasses_When_OrderIsReversed()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "--classes", "Ns.A", "Scenarios.dll", "--worker"]);

        Assert.Null(result.Error);
        Assert.True(result.Arguments!.Worker);
        Assert.Equal(["Ns.A"], result.Arguments.Classes);
    }

    [Fact]
    public void Parse_Should_SetParallelWithoutDegree_When_NoValueGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel"]);

        Assert.Null(result.Error);
        Assert.True(result.Arguments!.Parallel);
        Assert.Null(result.Arguments.ParallelDegree);
    }

    [Fact]
    public void Parse_Should_CaptureDegree_When_ParallelValueIsSeparateToken()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "4"]);

        Assert.Null(result.Error);
        Assert.True(result.Arguments!.Parallel);
        Assert.Equal(4, result.Arguments.ParallelDegree);
    }

    [Fact]
    public void Parse_Should_CaptureDegree_When_ParallelUsesEqualsSyntax()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel=3"]);

        Assert.Equal(3, result.Arguments!.ParallelDegree);
    }

    [Fact]
    public void Parse_Should_LeaveNextTokenForThePositionalSlot_When_ParallelValueIsNotAnInteger()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "--parallel", "Scenarios.dll"]);

        Assert.Null(result.Error);
        Assert.True(result.Arguments!.Parallel);
        Assert.Null(result.Arguments.ParallelDegree);
        Assert.Equal("Scenarios.dll", result.Arguments.AssemblyPath);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    public void Parse_Should_Fail_When_ParallelDegreeIsBelowOne(string degree)
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", degree]);

        Assert.NotNull(result.Error);
        Assert.Contains(">= 1", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_ParallelInlineValueIsNotANumber()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel=four"]);

        Assert.NotNull(result.Error);
        Assert.Contains(">= 1", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_ParallelCombinedWithWorker()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--worker"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--parallel is incompatible with --worker", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_ParallelCombinedWithList()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--list"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--parallel is incompatible with --list", result.Error);
    }

    [Fact]
    public void Parse_Should_CaptureWorkerTimeout_When_ParallelGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["run", "Scenarios.dll", "--parallel", "--worker-timeout", "30"]);

        Assert.Null(result.Error);
        Assert.Equal(30, result.Arguments!.WorkerTimeoutSeconds);
    }

    [Fact]
    public void Parse_Should_CaptureWorkerTimeout_When_TimeoutUsesEqualsSyntax()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--worker-timeout=45"]);

        Assert.Equal(45, result.Arguments!.WorkerTimeoutSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void Parse_Should_Fail_When_WorkerTimeoutValueIsInvalid(string value)
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["run", "Scenarios.dll", "--parallel", "--worker-timeout", value]);

        Assert.NotNull(result.Error);
        Assert.Contains("whole number of seconds >= 1", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_WorkerTimeoutValueIsMissing()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--worker-timeout"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--worker-timeout", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_WorkerTimeoutGivenWithoutParallel()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--worker-timeout", "30"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--worker-timeout requires --parallel", result.Error);
    }

    [Fact]
    public void Parse_Should_CaptureTrxPath_When_ParallelGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--trx", "out/run.trx"]);

        Assert.Null(result.Error);
        Assert.Equal("out/run.trx", result.Arguments!.TrxPath);
    }

    [Fact]
    public void Parse_Should_CaptureTrxPath_When_TrxUsesEqualsSyntax()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--trx=run.trx"]);

        Assert.Equal("run.trx", result.Arguments!.TrxPath);
    }

    [Fact]
    public void Parse_Should_Fail_When_TrxValueIsMissing()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--trx"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--trx requires a file path", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_TrxInlineValueIsEmpty()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel", "--trx="]);

        Assert.NotNull(result.Error);
        Assert.Contains("--trx requires a file path", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_TrxGivenWithoutParallel()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--trx", "run.trx"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--trx requires --parallel", result.Error);
    }

    [Fact]
    public void Parse_Should_ShowHelp_When_HelpTokenFollowsFixtureCommand()
    {
        Assert.True(CliArgumentsParser.Parse(["fixture", "--help"]).ShowHelp);
    }

    [Fact]
    public void Parse_Should_ReturnFixtureArguments_When_EveryRequiredOptionGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["fixture", "Scenarios.dll", "--scenario", "BuildsTheWorld", "--out", "fixtures/world.vcdbs"]);

        Assert.Null(result.Error);
        Assert.Null(result.Arguments);
        Assert.Equal(
            new FixtureArguments("Scenarios.dll", "BuildsTheWorld", "fixtures/world.vcdbs"),
            result.Fixture);
    }

    [Fact]
    public void Parse_Should_CaptureFixtureValues_When_OptionsUseEqualsSyntaxAndAnyOrder()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["fixture", "--out=world.vcdbs", "--force", "Scenarios.dll", "--scenario=Builder"]);

        Assert.Equal(
            new FixtureArguments("Scenarios.dll", "Builder", "world.vcdbs", Force: true),
            result.Fixture);
    }

    [Fact]
    public void Parse_Should_Fail_When_FixtureAssemblyPathIsMissing()
    {
        CliParseResult result = CliArgumentsParser.Parse(["fixture", "--scenario", "B", "--out", "w.vcdbs"]);

        Assert.NotNull(result.Error);
        Assert.Contains("assembly", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_ScenarioOptionIsAbsent()
    {
        CliParseResult result = CliArgumentsParser.Parse(["fixture", "Scenarios.dll", "--out", "w.vcdbs"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--scenario is required", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_OutOptionIsAbsent()
    {
        CliParseResult result = CliArgumentsParser.Parse(["fixture", "Scenarios.dll", "--scenario", "B"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--out is required", result.Error);
    }

    [Theory]
    [InlineData("--scenario")]
    [InlineData("--out")]
    public void Parse_Should_Fail_When_FixtureValueOptionHasNoValue(string option)
    {
        CliParseResult result = CliArgumentsParser.Parse(["fixture", "Scenarios.dll", option]);

        Assert.NotNull(result.Error);
        Assert.Contains($"{option} requires", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_FixtureGetsARunOnlyOption()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["fixture", "Scenarios.dll", "--scenario", "B", "--out", "w.vcdbs", "--parallel"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--parallel", result.Error);
        Assert.Contains("fixture", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_RunGetsAFixtureOnlyOption()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--out", "w.vcdbs"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--out", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_ForceCarriesAnInlineValue()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["fixture", "Scenarios.dll", "--scenario", "B", "--out", "w.vcdbs", "--force=yes"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--force=yes", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_TwoFixtureAssemblyPathsGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["fixture", "A.dll", "B.dll", "--scenario", "B", "--out", "w.vcdbs"]);

        Assert.NotNull(result.Error);
        Assert.Contains("B.dll", result.Error);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("version")]
    public void Parse_Should_ShowVersion_When_VersionTokenIsFirst(string token)
    {
        CliParseResult result = CliArgumentsParser.Parse([token]);

        Assert.True(result.ShowVersion);
        Assert.False(result.ShowHelp);
        Assert.Null(result.Arguments);
        Assert.Null(result.Fixture);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("fixture")]
    [InlineData("diff")]
    public void Parse_Should_ShowVersion_When_VersionTokenFollowsACommand(string command)
    {
        Assert.True(CliArgumentsParser.Parse([command, "--version"]).ShowVersion);
    }

    [Fact]
    public void Parse_Should_ShowVersion_When_VersionTokenIsMixedWithOtherRunArguments()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--filter", "Chest", "--version"]);

        Assert.True(result.ShowVersion);
        Assert.Null(result.Arguments);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Parse_Should_ShowVersion_When_VersionTokenPrecedesAUsageError()
    {
        Assert.True(CliArgumentsParser.Parse(["run", "--version", "--warp"]).ShowVersion);
    }

    [Fact]
    public void Parse_Should_PreferTheFirstSpecialToken_When_HelpComesBeforeVersion()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "--help", "--version"]);

        Assert.True(result.ShowHelp);
        Assert.False(result.ShowVersion);
    }

    [Fact]
    public void Parse_Should_PreferTheFirstSpecialToken_When_VersionComesBeforeHelp()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "--version", "--help"]);

        Assert.True(result.ShowVersion);
        Assert.False(result.ShowHelp);
    }

    [Fact]
    public void Parse_Should_Fail_When_VersionCarriesAnInlineValue()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--version=1"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--version=1", result.Error);
    }

    [Fact]
    public void Parse_Should_NotShowVersion_When_RunParsesSuccessfully()
    {
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll"]);

        Assert.False(result.ShowVersion);
    }

    [Fact]
    public void Parse_Should_CombineEveryParallelOption_When_AllAreGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            ["run", "Scenarios.dll", "--parallel", "3", "--worker-timeout", "120", "--trx", "r.trx"]);

        Assert.Null(result.Error);
        Assert.Equal(
            new CliArguments(
                "Scenarios.dll",
                Filter: null,
                List: false,
                Worker: false,
                Classes: null,
                Parallel: true,
                ParallelDegree: 3,
                WorkerTimeoutSeconds: 120,
                TrxPath: "r.trx"),
            result.Arguments);
    }

    [Fact]
    public void Parse_Should_MapThePositionalPathsInOrder_When_DiffIsInvoked()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "base.trx", "cand.trx"]);

        Assert.Null(result.Error);
        Assert.Equal(new DiffArguments("base.trx", "cand.trx"), result.Diff);
        Assert.Null(result.Arguments);
        Assert.Null(result.Fixture);
    }

    [Fact]
    public void Parse_Should_SetJson_When_DiffJsonFlagGivenAnywhere()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "--json", "base.trx", "cand.trx"]);

        Assert.Equal(new DiffArguments("base.trx", "cand.trx", Json: true), result.Diff);
    }

    [Fact]
    public void Parse_Should_SetJsonTests_When_DiffJsonTestsFlagGivenAnywhere()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "--json-tests", "base.trx", "cand.trx"]);

        Assert.Equal(new DiffArguments("base.trx", "cand.trx", JsonTests: true), result.Diff);
        Assert.True(result.Diff!.EmitJson);
    }

    [Fact]
    public void Parse_Should_SetBothFlags_When_DiffJsonAndJsonTestsAreBothGiven()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "--json", "--json-tests", "base.trx", "cand.trx"]);

        Assert.Equal(new DiffArguments("base.trx", "cand.trx", Json: true, JsonTests: true), result.Diff);
    }

    [Fact]
    public void Parse_Should_Fail_When_DiffJsonTestsCarriesAnInlineValue()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "a.trx", "b.trx", "--json-tests=yes"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--json-tests=yes", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_DiffGetsNoPaths()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff"]);

        Assert.NotNull(result.Error);
        Assert.Contains("atlas diff baseline.trx candidate.trx", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_DiffGetsOnlyTheBaselinePath()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "base.trx"]);

        Assert.NotNull(result.Error);
        Assert.Contains("candidate", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_DiffGetsAThirdPath()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "a.trx", "b.trx", "c.trx"]);

        Assert.NotNull(result.Error);
        Assert.Contains("c.trx", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_DiffGetsAnUnknownOption()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "a.trx", "b.trx", "--html"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--html", result.Error);
        Assert.Contains("diff", result.Error);
    }

    [Fact]
    public void Parse_Should_Fail_When_DiffJsonCarriesAnInlineValue()
    {
        CliParseResult result = CliArgumentsParser.Parse(["diff", "a.trx", "b.trx", "--json=yes"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--json=yes", result.Error);
    }

    [Fact]
    public void Parse_Should_ShowHelp_When_HelpTokenFollowsDiffCommand()
    {
        Assert.True(CliArgumentsParser.Parse(["diff", "--help"]).ShowHelp);
    }

    [Fact]
    public void DiffArguments_EmitJson_Should_BeTrue_When_EitherJsonFlagIsSet()
    {
        Assert.False(new DiffArguments("b", "c").EmitJson);
        Assert.True(new DiffArguments("b", "c", Json: true).EmitJson);
        Assert.True(new DiffArguments("b", "c", JsonTests: true).EmitJson);
        Assert.True(new DiffArguments("b", "c", Json: true, JsonTests: true).EmitJson);
    }
}
