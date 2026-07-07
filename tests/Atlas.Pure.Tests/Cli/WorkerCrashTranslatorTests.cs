using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class WorkerCrashTranslatorTests
{
    private const string ClassName = "Ns.ChestScenarios";

    [Fact]
    public void Translate_Should_SynthesizeTimeoutFailure_When_WorkerTimedOut()
    {
        var exit = new WorkerExit(ExitCode: null, TimedOut: true, "boot chatter");

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, Observed(), exit, timeoutSeconds: 45);

        Assert.Equal($"{ClassName} (worker timed out)", outcome!.TestName);
        Assert.Equal(ClassName, outcome.ClassName);
        Assert.Equal(TestOutcomeKind.Failed, outcome.Kind);
        Assert.Contains("exceeded its 45 s timeout", outcome.Message);
        Assert.Contains("stderr tail:", outcome.Message);
        Assert.Contains("boot chatter", outcome.Message);
    }

    [Fact]
    public void Translate_Should_SynthesizeCrashFailure_When_RunEndNeverArrived()
    {
        var exit = new WorkerExit(ExitCode: 139, TimedOut: false, "Segmentation fault");

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, Observed(), exit, timeoutSeconds: 600);

        Assert.Equal($"{ClassName} (worker crashed)", outcome!.TestName);
        Assert.Contains("exited with code 139 without a well-formed run-end", outcome.Message);
        Assert.Contains("Segmentation fault", outcome.Message);
    }

    [Fact]
    public void Translate_Should_ExplainTheUnknownExitCode_When_ProcessCouldNotRun()
    {
        var exit = new WorkerExit(ExitCode: null, TimedOut: false, "no such file");

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, Observed(), exit, timeoutSeconds: 600);

        Assert.Contains("unknown (the process could not run)", outcome!.Message);
    }

    [Fact]
    public void Translate_Should_UseReportedErrors_When_WorkerExitedNonZeroWithoutFailingScenario()
    {
        WorkerClassObservation observation = Observed(
            """{"v":1,"type":"error","message":"EnvironmentError: VINTAGE_STORY not set"}""",
            """{"v":1,"type":"run-end","exitCode":2}""");
        var exit = new WorkerExit(ExitCode: 2, TimedOut: false, string.Empty);

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, observation, exit, timeoutSeconds: 600);

        Assert.Equal($"{ClassName} (worker failed)", outcome!.TestName);
        Assert.Equal("EnvironmentError: VINTAGE_STORY not set", outcome.Message);
    }

    [Fact]
    public void Translate_Should_SynthesizeGenericFailure_When_NonZeroExitCarriesNoExplanation()
    {
        WorkerClassObservation observation = Observed("""{"v":1,"type":"run-end","exitCode":1}""");
        var exit = new WorkerExit(ExitCode: 1, TimedOut: false, string.Empty);

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, observation, exit, timeoutSeconds: 600);

        Assert.Contains("exited with code 1 without reporting a failing scenario", outcome!.Message);
    }

    [Fact]
    public void Translate_Should_ReturnNull_When_WorkerSucceeded()
    {
        WorkerClassObservation observation = Observed(
            """{"v":1,"type":"test-pass","class":"Ns.ChestScenarios","test":"Ns.ChestScenarios.T","durationMs":5}""",
            """{"v":1,"type":"run-end","exitCode":0}""");
        var exit = new WorkerExit(ExitCode: 0, TimedOut: false, "server chatter");

        Assert.Null(WorkerCrashTranslator.Translate(ClassName, observation, exit, timeoutSeconds: 600));
    }

    [Fact]
    public void Translate_Should_ReturnNull_When_TheWorkerAlreadyReportedItsFailures()
    {
        WorkerClassObservation observation = Observed(
            """{"v":1,"type":"test-fail","class":"Ns.ChestScenarios","test":"Ns.ChestScenarios.T","message":"Boom"}""",
            """{"v":1,"type":"run-end","exitCode":1}""");
        var exit = new WorkerExit(ExitCode: 1, TimedOut: false, string.Empty);

        Assert.Null(WorkerCrashTranslator.Translate(ClassName, observation, exit, timeoutSeconds: 600));
    }

    [Fact]
    public void Translate_Should_KeepOnlyTheLastLines_When_StderrIsLong()
    {
        string stderr = string.Join('\n', Enumerable.Range(0, 40).Select(i => $"line-{i}"));
        var exit = new WorkerExit(ExitCode: null, TimedOut: true, stderr);

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, Observed(), exit, timeoutSeconds: 600);

        Assert.Contains("line-39", outcome!.Message);
        Assert.Contains("line-25", outcome.Message);
        Assert.DoesNotContain("line-24", outcome.Message);
    }

    [Fact]
    public void Translate_Should_CapTheTailLength_When_StderrLinesAreHuge()
    {
        var exit = new WorkerExit(ExitCode: null, TimedOut: true, new string('x', 5000));

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, Observed(), exit, timeoutSeconds: 600);

        Assert.Contains("stderr tail:", outcome!.Message);
        Assert.True(outcome.Message!.Length < 2500, $"tail was not capped: {outcome.Message.Length} chars");
    }

    [Fact]
    public void Translate_Should_OmitTheStderrSection_When_StderrIsBlank()
    {
        var exit = new WorkerExit(ExitCode: null, TimedOut: true, "  \n ");

        TestOutcome? outcome = WorkerCrashTranslator.Translate(ClassName, Observed(), exit, timeoutSeconds: 600);

        Assert.DoesNotContain("stderr tail:", outcome!.Message);
    }

    private static WorkerClassObservation Observed(params string[] lines)
    {
        var observation = new WorkerClassObservation();
        foreach (string line in lines)
        {
            observation.AcceptLine(line);
        }

        return observation;
    }
}
