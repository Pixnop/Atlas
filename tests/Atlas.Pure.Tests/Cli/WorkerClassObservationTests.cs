using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class WorkerClassObservationTests
{
    [Fact]
    public void AcceptLine_Should_ReturnPassedOutcome_When_TestPassEventArrives()
    {
        var observation = new WorkerClassObservation();

        TestOutcome? outcome = observation.AcceptLine(
            """{"v":1,"type":"test-pass","class":"Ns.A","test":"Ns.A.Chest_survives","durationMs":42}""");

        Assert.Equal(new TestOutcome("Ns.A", "Ns.A.Chest_survives", TestOutcomeKind.Passed, 42), outcome);
        Assert.Same(outcome, Assert.Single(observation.Tests));
    }

    [Fact]
    public void AcceptLine_Should_CarryMessageAndStack_When_TestFailEventArrives()
    {
        var observation = new WorkerClassObservation();

        TestOutcome? outcome = observation.AcceptLine(
            """{"v":1,"type":"test-fail","class":"Ns.A","test":"Ns.A.T","durationMs":7,"message":"Boom: no","stack":"at Ns.A.T()"}""");

        Assert.Equal(TestOutcomeKind.Failed, outcome!.Kind);
        Assert.Equal("Boom: no", outcome.Message);
        Assert.Equal("at Ns.A.T()", outcome.Stack);
        Assert.True(observation.SawFailure);
    }

    [Fact]
    public void AcceptLine_Should_DefaultTheMessage_When_TestFailEventOmitsIt()
    {
        var observation = new WorkerClassObservation();

        TestOutcome? outcome = observation.AcceptLine("""{"v":1,"type":"test-fail","class":"Ns.A","test":"Ns.A.T"}""");

        Assert.Equal("unknown failure", outcome!.Message);
        Assert.Equal(0, outcome.DurationMs);
    }

    [Fact]
    public void AcceptLine_Should_CarryTheReason_When_TestSkipEventArrives()
    {
        var observation = new WorkerClassObservation();

        TestOutcome? outcome = observation.AcceptLine(
            """{"v":1,"type":"test-skip","class":"Ns.A","test":"Ns.A.T","durationMs":0,"reason":"not today"}""");

        Assert.Equal(TestOutcomeKind.Skipped, outcome!.Kind);
        Assert.Equal("not today", outcome.Message);
        Assert.False(observation.SawFailure);
    }

    [Fact]
    public void AcceptLine_Should_DefaultTheReason_When_TestSkipEventOmitsIt()
    {
        var observation = new WorkerClassObservation();

        TestOutcome? outcome = observation.AcceptLine("""{"v":1,"type":"test-skip","class":"Ns.A","test":"Ns.A.T"}""");

        Assert.Equal("no reason given", outcome!.Message);
    }

    [Fact]
    public void AcceptLine_Should_AccumulateErrors_When_ErrorEventsArrive()
    {
        var observation = new WorkerClassObservation();

        Assert.Null(observation.AcceptLine("""{"v":1,"type":"error","message":"EnvError: install missing"}"""));
        Assert.Null(observation.AcceptLine("""{"v":1,"type":"error"}"""));

        Assert.Equal(["EnvError: install missing", "unknown worker error"], observation.Errors);
    }

    [Fact]
    public void AcceptLine_Should_AccumulateClassSummaries_When_ClassSummaryEventsArrive()
    {
        var observation = new WorkerClassObservation();

        Assert.Null(observation.AcceptLine(
            """{"v":1,"type":"class-summary","class":"Ns.A","summary":"[Atlas] isolation summary for Ns.A: 1 restart(s) (7.1 s total)."}"""));

        WorkerClassSummary summary = Assert.Single(observation.ClassSummaries);
        Assert.Equal("Ns.A", summary.ClassName);
        Assert.Contains("1 restart(s) (7.1 s total)", summary.Summary);
        Assert.Empty(observation.Tests); // a summary is not a test outcome
    }

    [Theory]
    [InlineData("""{"v":1,"type":"class-summary","class":"Ns.A"}""")]
    [InlineData("""{"v":1,"type":"class-summary","class":"Ns.A","summary":""}""")]
    public void AcceptLine_Should_DropTheClassSummary_When_ItCarriesNoUsableText(string line)
    {
        var observation = new WorkerClassObservation();

        Assert.Null(observation.AcceptLine(line));

        Assert.Empty(observation.ClassSummaries);
    }

    [Fact]
    public void AcceptLine_Should_SetSawRunEnd_When_RunEndArrives()
    {
        var observation = new WorkerClassObservation();
        Assert.False(observation.SawRunEnd);

        Assert.Null(observation.AcceptLine(
            """{"v":1,"type":"run-end","total":1,"passed":1,"failed":0,"skipped":0,"errors":0,"wallClockMs":5,"exitCode":0}"""));

        Assert.True(observation.SawRunEnd);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"type\":")]
    [InlineData("42")]
    [InlineData("[1,2]")]
    public void AcceptLine_Should_IgnoreLine_When_LineIsNotAProtocolObject(string line)
    {
        var observation = new WorkerClassObservation();

        Assert.Null(observation.AcceptLine(line));
        Assert.Empty(observation.Tests);
        Assert.Empty(observation.Errors);
        Assert.False(observation.SawRunEnd);
    }

    [Theory]
    [InlineData("""{"v":1,"type":"run-start","assembly":"S.dll","pid":1}""")]
    [InlineData("""{"v":1,"type":"class-start","class":"Ns.A"}""")]
    [InlineData("""{"v":1,"type":"some-future-event","class":"Ns.A"}""")]
    public void AcceptLine_Should_ReturnNull_When_EventCarriesNoOutcome(string line)
    {
        var observation = new WorkerClassObservation();

        Assert.Null(observation.AcceptLine(line));
        Assert.Empty(observation.Tests);
    }

    [Fact]
    public void AcceptLine_Should_DefaultMissingFields_When_EventIsSparse()
    {
        var observation = new WorkerClassObservation();

        TestOutcome? outcome = observation.AcceptLine("""{"type":"test-pass"}""");

        Assert.Equal(new TestOutcome(string.Empty, string.Empty, TestOutcomeKind.Passed, 0), outcome);
    }
}
