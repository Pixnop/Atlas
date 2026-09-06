using Atlas.Internal.Player;

namespace Atlas.Pure.Tests.Player;

/// <summary>The kicked-player recheck countdown, checked without a server: what it does with
/// each outcome, that it finishes at most once whichever trigger got there first, and the
/// give-up branch, which needs a teardown that never settles and so has never been reachable
/// from a live kick.</summary>
public class RecheckLoopTests
{
    private const int MaxInFlightRechecks = 100;
    private const string Subject = "test player StalledKick (client 3)";

    [Fact]
    public void Run_Should_FinishOnce_When_TwoTriggersBothSeeTheRemoval()
    {
        // Both triggers arm their own chain (a PlayerDisconnect and the arm-time safety net),
        // and both can reach a settled removal; the joined-name claim must be released once.
        int removed = 0;
        var loop = new KickedPlayerCleanup.RecheckLoop(
            Subject,
            _ => KickedPlayerCleanup.SettleOutcome.Removed,
            NoSchedule,
            () => removed++,
            NoGiveUp);

        loop.Run(disconnectObserved: true, MaxInFlightRechecks);
        loop.Run(disconnectObserved: false, rechecksLeft: 0);

        Assert.Equal(1, removed);
    }

    [Fact]
    public void Run_Should_StopChecking_When_AlreadyFinalized()
    {
        int checks = 0;
        var loop = new KickedPlayerCleanup.RecheckLoop(
            Subject,
            _ =>
            {
                checks++;
                return KickedPlayerCleanup.SettleOutcome.Removed;
            },
            NoSchedule,
            () => { },
            NoGiveUp);

        loop.Run(disconnectObserved: true, MaxInFlightRechecks);
        loop.Run(disconnectObserved: true, MaxInFlightRechecks);

        Assert.Equal(1, checks);
    }

    [Fact]
    public void Run_Should_DoNothing_When_ThePlayerIsHealthy()
    {
        int scheduled = 0;
        int removed = 0;
        var loop = new KickedPlayerCleanup.RecheckLoop(
            Subject,
            _ => KickedPlayerCleanup.SettleOutcome.Healthy,
            _ => scheduled++,
            () => removed++,
            NoGiveUp);

        loop.Run(disconnectObserved: false, rechecksLeft: 0);

        Assert.Equal(0, scheduled);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Run_Should_RescheduleAsTheObservedVariant_When_TheTeardownIsStillInFlight()
    {
        // The re-check always claims an observed disconnect: the first check may have been the
        // arm-time safety net, but a TeardownInFlight verdict proves one was seen.
        var observed = new List<bool>();
        var pending = new List<Action>();
        var loop = new KickedPlayerCleanup.RecheckLoop(
            Subject,
            disconnectObserved =>
            {
                observed.Add(disconnectObserved);
                return KickedPlayerCleanup.SettleOutcome.TeardownInFlight;
            },
            pending.Add,
            () => { },
            _ => { }); // the second check exhausts the one allowance and gives up; not this test's subject

        loop.Run(disconnectObserved: false, rechecksLeft: 1);
        Assert.Single(pending);
        pending[0]();

        Assert.Equal([false, true], observed);
    }

    [Fact]
    public void Run_Should_RescheduleExactlyMaxTimesThenGiveUp_When_TheTeardownNeverSettles()
    {
        int scheduled = 0;
        var giveUp = new List<string>();
        var loop = new KickedPlayerCleanup.RecheckLoop(
            Subject,
            _ => KickedPlayerCleanup.SettleOutcome.TeardownInFlight,
            retry =>
            {
                scheduled++;
                retry();
            },
            () => Assert.Fail("a never-settling teardown must not report the player as removed"),
            giveUp.Add);

        loop.Run(disconnectObserved: true, MaxInFlightRechecks);

        Assert.Equal(MaxInFlightRechecks, scheduled);
        string line = Assert.Single(giveUp);
        Assert.Contains(Subject, line);
        Assert.Contains($"after {MaxInFlightRechecks} re-checks", line);
    }

    private static void NoSchedule(Action retry)
        => Assert.Fail("a settled outcome must not schedule a re-check");

    private static void NoGiveUp(string message) => Assert.Fail("unexpected give-up: " + message);
}
