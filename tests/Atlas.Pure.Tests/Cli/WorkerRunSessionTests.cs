using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class WorkerRunSessionTests
{
    [Fact]
    public void Start_Should_DescribeTheAssignment_When_ClassesAreGiven()
    {
        WorkerEvent evt = NewSession(["Ns.A", "Ns.B"]).Start();

        var start = Assert.IsType<RunStartEvent>(evt);
        Assert.Equal("/tmp/S.dll", start.Assembly);
        Assert.Equal(["Ns.A", "Ns.B"], start.Classes);
        Assert.Equal(42, start.Pid);
        Assert.Equal("0.5.0", start.AtlasVersion);
    }

    [Fact]
    public void RecordPass_Should_OpenTheClass_When_FirstResultArrives()
    {
        WorkerRunSession session = NewSession();

        IReadOnlyList<WorkerEvent> events = session.RecordPass("Ns.A", "Ns.A.S1", 1.5m);

        Assert.Collection(
            events,
            evt => Assert.Equal("Ns.A", Assert.IsType<ClassStartEvent>(evt).Class),
            evt =>
            {
                var pass = Assert.IsType<TestPassEvent>(evt);
                Assert.Equal("Ns.A.S1", pass.Test);
                Assert.Equal(1500, pass.DurationMs);
            });
    }

    [Fact]
    public void RecordPass_Should_NotReopenTheClass_When_ResultIsForTheSameClass()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);

        IReadOnlyList<WorkerEvent> events = session.RecordPass("Ns.A", "Ns.A.S2", 1m);

        WorkerEvent evt = Assert.Single(events);
        Assert.IsType<TestPassEvent>(evt);
    }

    [Fact]
    public void RecordPass_Should_CloseThePreviousClass_When_TheClassChanges()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);
        session.RecordSkip("Ns.A", "Ns.A.S2", "later");

        IReadOnlyList<WorkerEvent> events = session.RecordPass("Ns.B", "Ns.B.S1", 1m);

        Assert.Collection(
            events,
            evt =>
            {
                var end = Assert.IsType<ClassEndEvent>(evt);
                Assert.Equal("Ns.A", end.Class);
                Assert.Equal(1, end.Passed);
                Assert.Equal(0, end.Failed);
                Assert.Equal(1, end.Skipped);
            },
            evt => Assert.Equal("Ns.B", Assert.IsType<ClassStartEvent>(evt).Class),
            evt => Assert.IsType<TestPassEvent>(evt));
    }

    [Fact]
    public void RecordFail_Should_CarryMessageAndStack_When_TheScenarioFails()
    {
        WorkerRunSession session = NewSession();

        IReadOnlyList<WorkerEvent> events = session.RecordFail("Ns.A", "Ns.A.S1", 0.25m, "BoomException", "it broke", "at Ns.A");

        var fail = Assert.IsType<TestFailEvent>(events[^1]);
        Assert.Equal("BoomException: it broke", fail.Message);
        Assert.Equal("at Ns.A", fail.Stack);
        Assert.Equal(250, fail.DurationMs);
    }

    [Fact]
    public void RecordFail_Should_DropTheStack_When_ItIsWhitespace()
    {
        WorkerRunSession session = NewSession();

        IReadOnlyList<WorkerEvent> events = session.RecordFail("Ns.A", "Ns.A.S1", 0m, "X", "boom", "  ");

        Assert.Null(Assert.IsType<TestFailEvent>(events[^1]).Stack);
    }

    [Fact]
    public void Complete_Should_CloseTheOpenClassAndEmitTotals_When_TheRunEnds()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);
        session.RecordFail("Ns.A", "Ns.A.S2", 1m, "X", "boom", null);

        IReadOnlyList<WorkerEvent> events = session.Complete(9000);

        Assert.Collection(
            events,
            evt => Assert.Equal("Ns.A", Assert.IsType<ClassEndEvent>(evt).Class),
            evt =>
            {
                var end = Assert.IsType<RunEndEvent>(evt);
                Assert.Equal(2, end.Total);
                Assert.Equal(1, end.Passed);
                Assert.Equal(1, end.Failed);
                Assert.Equal(0, end.Skipped);
                Assert.Equal(0, end.Errors);
                Assert.Equal(9000, end.WallClockMs);
                Assert.Equal(1, end.ExitCode);
            });
    }

    [Fact]
    public void Complete_Should_BeIdempotent_When_CalledTwice()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);
        session.Complete(1);

        Assert.Empty(session.Complete(2));
    }

    [Fact]
    public void ExitCode_Should_BeZero_When_EveryScenarioPassed()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);

        Assert.Equal(0, session.ExitCode);
    }

    [Fact]
    public void ExitCode_Should_BeOne_When_NothingRan()
    {
        Assert.Equal(1, NewSession().ExitCode);
    }

    [Fact]
    public void ExitCode_Should_BeOne_When_ARunnerErrorOccurred()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);

        IReadOnlyList<WorkerEvent> events = session.RecordError("FixtureException", "fixture died");

        Assert.Equal("FixtureException: fixture died", Assert.IsType<ErrorEvent>(Assert.Single(events)).Message);
        Assert.Equal(1, session.ExitCode);
    }

    [Fact]
    public void Complete_Should_UseTheOverride_When_AnExitCodeIsForced()
    {
        WorkerRunSession session = NewSession();
        session.RecordError("EnvironmentError", "VINTAGE_STORY is not set");

        IReadOnlyList<WorkerEvent> events = session.Complete(3, exitCode: 2);

        Assert.Equal(2, Assert.IsType<RunEndEvent>(Assert.Single(events)).ExitCode);
    }

    [Fact]
    public void RecordCrash_Should_FailTheInFlightClass_When_AClassIsOpen()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);

        IReadOnlyList<WorkerEvent> crashEvents = session.RecordCrash("the server died");
        IReadOnlyList<WorkerEvent> closing = session.Complete(50);

        var fail = Assert.IsType<TestFailEvent>(Assert.Single(crashEvents));
        Assert.Equal("Ns.A", fail.Class);
        Assert.Equal("the server died", fail.Message);
        var classEnd = Assert.IsType<ClassEndEvent>(closing[0]);
        Assert.Equal(1, classEnd.Failed);
        Assert.Equal(1, Assert.IsType<RunEndEvent>(closing[^1]).ExitCode);
    }

    [Fact]
    public void RecordCrash_Should_ReportAnError_When_NoClassIsOpen()
    {
        WorkerRunSession session = NewSession();

        IReadOnlyList<WorkerEvent> events = session.RecordCrash("early death");

        Assert.Equal("early death", Assert.IsType<ErrorEvent>(Assert.Single(events)).Message);
        Assert.Equal(1, session.ExitCode);
    }

    [Fact]
    public void RecordClassSummary_Should_EmitTheEventWithoutTransitions_When_TheClassIsOpen()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);

        IReadOnlyList<WorkerEvent> events = session.RecordClassSummary(
            "Ns.A", "[Atlas] isolation summary for Ns.A: 1 restart(s) (7.1 s total).");

        // No class transition and no counting: the summary rides between the class's last test
        // event and its class-end line, and the totals are untouched.
        var summary = Assert.IsType<ClassSummaryEvent>(Assert.Single(events));
        Assert.Equal("Ns.A", summary.Class);
        Assert.Contains("1 restart(s)", summary.Summary);
        var end = Assert.IsType<ClassEndEvent>(session.Complete(1)[0]);
        Assert.Equal(1, end.Passed);
    }

    [Fact]
    public void RecordClassSummary_Should_EmitNothing_When_TheStreamIsAlreadyClosed()
    {
        WorkerRunSession session = NewSession();
        session.RecordPass("Ns.A", "Ns.A.S1", 1m);
        session.Complete(1);

        Assert.Empty(session.RecordClassSummary("Ns.A", "[Atlas] isolation summary for Ns.A: late."));
    }

    [Fact]
    public void RecordSkip_Should_CountTowardsTotals_When_TheScenarioIsSkipped()
    {
        WorkerRunSession session = NewSession();
        session.RecordSkip("Ns.A", "Ns.A.S1", "not today");

        IReadOnlyList<WorkerEvent> events = session.Complete(1);

        var end = Assert.IsType<RunEndEvent>(events[^1]);
        Assert.Equal(1, end.Total);
        Assert.Equal(1, end.Skipped);
        Assert.Equal(0, end.ExitCode);
    }

    private static WorkerRunSession NewSession(IReadOnlyList<string>? classes = null) =>
        new("/tmp/S.dll", classes, 42, "0.5.0");
}
