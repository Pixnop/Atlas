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
        var ex = Assert.Throws<AggregateException>(() => run.Wait(TimeSpan.FromSeconds(1)));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    private static void Pump(GameThreadScheduler s, Task until)
    {
        while (!until.IsCompleted)
        {
            s.DrainPending();
        }
    }
}
