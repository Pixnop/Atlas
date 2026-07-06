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
}
