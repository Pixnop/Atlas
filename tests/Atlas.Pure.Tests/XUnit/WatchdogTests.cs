namespace Atlas.Pure.Tests.XUnit;

using Atlas.Api;
using Atlas.XUnit.Internal;

public class WatchdogTests
{
    [Fact]
    public async Task RunAsync_Should_ThrowScenarioTimeout_When_ScenarioNeverCompletes()
    {
        var never = new TaskCompletionSource().Task;
        await Assert.ThrowsAsync<ScenarioTimeoutException>(
            () => Watchdog.RunAsync(never, timeoutMs: 50, currentTick: () => 42));
    }

    [Fact]
    public async Task RunAsync_Should_PropagateScenarioException_When_ScenarioFails()
    {
        Task failing = Task.FromException(new InvalidOperationException("scenario bug"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Watchdog.RunAsync(failing, timeoutMs: 1000, currentTick: () => 0));
    }

    [Fact]
    public async Task RunAsync_Should_CarryCurrentTick_When_TimeoutFires()
    {
        var never = new TaskCompletionSource().Task;
        ScenarioTimeoutException ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(
            () => Watchdog.RunAsync(never, timeoutMs: 50, currentTick: () => 42));
        Assert.Equal(42, ex.TicksWaited);
    }

    [Fact]
    public async Task RunAsync_Should_Complete_When_ScenarioFinishesBeforeTimeout()
    {
        Task fast = Task.CompletedTask;
        await Watchdog.RunAsync(fast, timeoutMs: 1000, currentTick: () => 0);
    }
}
