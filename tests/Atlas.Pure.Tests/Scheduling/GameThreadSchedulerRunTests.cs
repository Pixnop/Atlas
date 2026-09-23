namespace Atlas.Pure.Tests.Scheduling;

public class GameThreadSchedulerRunTests
{
    [Fact]
    public void RunAsync_Should_ExecuteBodyAndContinuationsOnPumpThread_When_Awaited()
    {
        var scheduler = GameThreadScheduler.InstallOnCurrentThread();
        int pumpThread = Environment.CurrentManagedThreadId;
        var seen = new List<int>();
        Task run = scheduler.RunAsync(async () =>
        {
            seen.Add(Environment.CurrentManagedThreadId);
            await Task.Yield();
            seen.Add(Environment.CurrentManagedThreadId);
        });
        Pump(scheduler, run);
        Assert.All(seen, tid => Assert.Equal(pumpThread, tid));
    }

    [Fact]
    public void RunAsync_Should_PropagateException_When_BodyThrows()
    {
        var scheduler = GameThreadScheduler.InstallOnCurrentThread();
        Task run = scheduler.RunAsync(() => throw new InvalidOperationException("boom"));
        Pump(scheduler, run);
        AggregateException ex = Assert.Throws<AggregateException>(() => run.Wait(TimeSpan.FromSeconds(1)));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void RunAsync_Should_InstallItselfDuringWork_And_RestoreTheAmbientContextAfter()
    {
        var scheduler = new GameThreadScheduler();
        var marker = new SynchronizationContext();
        SynchronizationContext? original = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(marker);
            SynchronizationContext? seenDuringWork = null;
            Task run = scheduler.RunAsync(() =>
            {
                seenDuringWork = SynchronizationContext.Current;
                return Task.CompletedTask;
            });

            scheduler.DrainPending();

            Assert.True(run.IsCompletedSuccessfully);
            Assert.Same(scheduler, seenDuringWork);

            // The caller's own ambient context (not null, not the scheduler) must come back once
            // the work is done, so a later await on this thread does not keep posting to a
            // finished scheduler.
            Assert.Same(marker, SynchronizationContext.Current);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    private static void Pump(GameThreadScheduler s, Task until)
    {
        while (!until.IsCompleted)
        {
            s.DrainPending();
        }
    }
}
