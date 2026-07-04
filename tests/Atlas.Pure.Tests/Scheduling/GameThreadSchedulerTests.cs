namespace Atlas.Pure.Tests.Scheduling;

public class GameThreadSchedulerTests
{
    private static readonly int[] FifoOrder = [1, 2];

    [Fact]
    public void DrainPending_Should_RunPostedCallbacks_When_Drained()
    {
        var scheduler = GameThreadScheduler.InstallOnCurrentThread();
        int calls = 0;
        scheduler.Post(_ => calls++, null);
        scheduler.Post(_ => calls++, null);
        Assert.Equal(0, calls);          // nothing runs before the pump drains
        scheduler.DrainPending();
        Assert.Equal(2, calls);
        Assert.False(scheduler.HasPending);
    }

    [Fact]
    public void DrainPending_Should_PreserveFifoOrder_When_MultiplePosted()
    {
        var scheduler = GameThreadScheduler.InstallOnCurrentThread();
        var order = new List<int>();
        scheduler.Post(_ => order.Add(1), null);
        scheduler.Post(_ => order.Add(2), null);
        scheduler.DrainPending();
        Assert.Equal(FifoOrder, order);
    }

    [Fact]
    public void DrainPending_Should_RunCallbackPostedDuringDrain_When_SameDrainCall()
    {
        var scheduler = GameThreadScheduler.InstallOnCurrentThread();
        int calls = 0;
        scheduler.Post(_ => scheduler.Post(_ => calls++, null), null);
        scheduler.DrainPending();
        Assert.Equal(1, calls);          // re-entrant posts drain in the same pass
    }
}
