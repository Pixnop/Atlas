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
        CliParseResult result = CliArgumentsParser.Parse(["run", "Scenarios.dll", "--parallel"]);

        Assert.NotNull(result.Error);
        Assert.Contains("--parallel", result.Error);
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
}
