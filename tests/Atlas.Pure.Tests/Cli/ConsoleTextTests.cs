using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

/// <summary>Covers what `atlas run` writes to the console: text a scenario supplied (a failure
/// message, a stack trace, captured output, a display name) can carry control characters, which
/// the TRX writes as visible escapes and the console now does the same way, so a payload printed
/// raw cannot move the cursor, clear the screen or ring the terminal.</summary>
public class ConsoleTextTests
{
    [Fact]
    public void WriteLine_Should_EscapeControlCharactersAsTheTrxDoes_When_ALineCarriesThem()
    {
        var output = new StringWriter();

        ConsoleText.WriteLine(output, "FAIL Suite.T\nred \u001b[31mtext\u001b[0m, bell \u0007, nul \u0000, bs \b");

        Assert.Equal(
            "FAIL Suite.T\nred \\u001B[31mtext\\u001B[0m, bell \\u0007, nul \\u0000, bs \\u0008" + output.NewLine,
            output.ToString());
        Assert.Equal("a\\u001Bb", XmlOutput.Escape("a\u001bb")); // the same notation as the TRX
    }

    [Fact]
    public void WriteLine_Should_KeepTabsLineBreaksAndOrdinaryText_When_NothingNeedsEscaping()
    {
        var output = new StringWriter();
        const string Line = "PASS [Suite] café \U0001F600\tone\r\ntwo\nthree";

        ConsoleText.WriteLine(output, Line);

        Assert.Equal(Line + output.NewLine, output.ToString());
    }

    [Fact]
    public void WriteLine_Should_EscapeALoneSurrogate_When_TheLineHoldsOne()
    {
        var output = new StringWriter();

        ConsoleText.WriteLine(output, "bad \ud800 pair");

        Assert.Equal("bad \\uD800 pair" + output.NewLine, output.ToString());
    }

    [Fact]
    public void WriteLine_Should_EscapeTheFailureBlockOfASequentialRun_When_TheMessageStackAndOutputCarryEscapes()
    {
        var report = new RunReport();
        var output = new StringWriter();

        ConsoleText.WriteLine(
            output,
            report.RecordFail(
                "Suite.\u001bTest",
                0.5m,
                "System.Exception",
                "message \u001b[2J",
                "   at Suite.Run() \u0007",
                "output \u0000"));

        string written = output.ToString();
        Assert.DoesNotContain('\u001b', written);
        Assert.DoesNotContain('\u0007', written);
        Assert.DoesNotContain('\u0000', written);
        Assert.Contains("FAIL Suite.\\u001BTest", written, StringComparison.Ordinal);
        Assert.Contains("message \\u001B[2J", written, StringComparison.Ordinal);
        Assert.Contains("at Suite.Run() \\u0007", written, StringComparison.Ordinal);
        Assert.Contains("output \\u0000", written, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_Should_EscapeTheFailureBlockOfAParallelRun_When_TheMessageAndStackCarryEscapes()
    {
        var report = new ParallelRunReport();
        var output = new StringWriter();

        ConsoleText.WriteLine(
            output,
            report.RecordTest(new TestOutcome(
                "Ns.Suite", "Ns.Suite.T", TestOutcomeKind.Failed, 100, "boom \u001b[2J", "   at Run() \u0007")));

        string written = output.ToString();
        Assert.DoesNotContain('\u001b', written);
        Assert.DoesNotContain('\u0007', written);
        Assert.Contains("boom \\u001B[2J", written, StringComparison.Ordinal);
        Assert.Contains("at Run() \\u0007", written, StringComparison.Ordinal);
    }
}
