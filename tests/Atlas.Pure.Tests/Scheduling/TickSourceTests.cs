namespace Atlas.Pure.Tests.Scheduling;

using Atlas.Api;
using Atlas.Internal.Scheduling;

public class TickSourceTests
{
    [Fact]
    public void WaitTicksAsync_Should_Complete_When_ExactTickCountRaised()
    {
        var source = new TickSource();
        Task wait = source.WaitTicksAsync(3);
        source.RaiseTick();
        source.RaiseTick();
        Assert.False(wait.IsCompleted);
        source.RaiseTick();
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public void WaitUntilAsync_Should_Complete_When_PredicateTurnsTrue()
    {
        var source = new TickSource();
        bool flag = false;
        Task wait = source.WaitUntilAsync(() => flag, timeoutTicks: 10);
        source.RaiseTick();
        Assert.False(wait.IsCompleted);
        flag = true;
        source.RaiseTick();
        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public void WaitUntilAsync_Should_ThrowScenarioTimeout_When_TimeoutTicksExceeded()
    {
        var source = new TickSource();
        Task wait = source.WaitUntilAsync(() => false, timeoutTicks: 2);
        source.RaiseTick();
        source.RaiseTick();
        var ex = Assert.IsType<ScenarioTimeoutException>(wait.Exception!.InnerException);
        Assert.Equal(2, ex.TicksWaited);
    }

    [Fact]
    public void WaitTicksAsync_Should_ServeMultipleWaiters_When_Interleaved()
    {
        var source = new TickSource();
        Task a = source.WaitTicksAsync(1);
        Task b = source.WaitTicksAsync(2);
        source.RaiseTick();
        Assert.True(a.IsCompletedSuccessfully);
        Assert.False(b.IsCompleted);
        source.RaiseTick();
        Assert.True(b.IsCompletedSuccessfully);
    }

    [Fact]
    public void TickCount_Should_Increment_When_RaiseTickIsCalled()
    {
        var source = new TickSource();
        source.RaiseTick();
        source.RaiseTick();
        Assert.Equal(2, source.TickCount);
    }
}
