using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

/// <summary>Contract of the issue #59 mitigation: dispose only a runner that idled, after a
/// grace; never dispose one that never idles (leak it, bounded), because disposing a busy
/// runner races its worker thread and the ObjectDisposedException kills the process.</summary>
public class RunnerDisposalTests
{
    [Fact]
    public void DisposeWhenIdle_Should_DisposeAfterOneGraceSleep_When_RunnerIsAlreadyIdle()
    {
        var sleeps = new List<TimeSpan>();
        int disposed = 0;

        bool result = RunnerDisposal.DisposeWhenIdle(() => true, () => disposed++, sleeps.Add);

        Assert.True(result);
        Assert.Equal(1, disposed);
        Assert.Equal([RunnerDisposal.DisposeGrace], sleeps);
    }

    [Fact]
    public void DisposeWhenIdle_Should_PollThenDispose_When_RunnerIdlesLate()
    {
        var sleeps = new List<TimeSpan>();
        int disposed = 0;
        int polls = 0;

        bool result = RunnerDisposal.DisposeWhenIdle(() => ++polls > 5, () => disposed++, sleeps.Add);

        Assert.True(result);
        Assert.Equal(1, disposed);
        Assert.Equal(6, sleeps.Count);
        Assert.All(sleeps.Take(5), s => Assert.Equal(RunnerDisposal.PollStep, s));
        Assert.Equal(RunnerDisposal.DisposeGrace, sleeps[^1]);
    }

    [Fact]
    public void DisposeWhenIdle_Should_SleepTheGraceBeforeDisposing_When_RunnerIdles()
    {
        var order = new List<string>();

        RunnerDisposal.DisposeWhenIdle(() => true, () => order.Add("dispose"), _ => order.Add("sleep"));

        Assert.Equal(["sleep", "dispose"], order);
    }

    [Fact]
    public void DisposeWhenIdle_Should_LeakWithoutDisposing_When_RunnerNeverIdles()
    {
        var sleeps = new List<TimeSpan>();
        int disposed = 0;

        bool result = RunnerDisposal.DisposeWhenIdle(() => false, () => disposed++, sleeps.Add);

        Assert.False(result);
        Assert.Equal(0, disposed);

        // The wait is bounded: it burns exactly the idle timeout in poll steps, then gives up.
        Assert.All(sleeps, s => Assert.Equal(RunnerDisposal.PollStep, s));
        Assert.Equal(RunnerDisposal.IdleTimeout, TimeSpan.FromTicks(sleeps.Sum(s => s.Ticks)));
    }
}
