using Atlas.Internal.Hosting;
using Vintagestory.API.Server;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>Pins the poll that stands in for the <c>PlayerReady</c> event, which never reaches a
/// mod (see <see cref="PlayingWatch"/>): the wait ends on the tick the connection state turns
/// <c>Playing</c>, no earlier, and not at all for a state that only looks close.</summary>
/// <remarks>The poll is driven with a state sequence and not with a substitute player: on 1.22.7
/// <c>IPlayer</c> has an internal member, which a proxy generator cannot implement, so no
/// <c>IServerPlayer</c> can be substituted there. The engine tests cover the player overload on
/// real players.</remarks>
public class PlayingWatchTests
{
    /// <summary>How long a completion is awaited after the last tick: the waits finish through a
    /// continuation on another thread, and a wait that never finishes must fail, not hang the run.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    [Fact]
    public void IsPlaying_Should_BeTrueForPlayingOnly_When_GivenEveryEngineState()
    {
        foreach (EnumClientState state in Enum.GetValues<EnumClientState>())
        {
            // The value is the loaded engine's own, never the literal (1.22 shifted the enum).
            Assert.Equal(state == EngineCompat.ClientStatePlaying, PlayingWatch.IsPlaying(state));
        }

        Assert.False(PlayingWatch.IsPlaying(EnumClientState.Connected));
    }

    [Fact]
    public async Task WaitForPlayingAsync_Should_CompleteOnTheTickTheStateTurnsPlaying_When_PolledEachTick()
    {
        var ticks = new TickSource();
        var states = new Queue<EnumClientState>(
            [EnumClientState.Connecting, EnumClientState.Connected, EnumClientState.Connected, EngineCompat.ClientStatePlaying]);

        Task wait = PlayingWatch.WaitForPlayingAsync(ticks, () => states.Dequeue(), timeoutTicks: 10);

        // Connected is where a client that withholds packet 29 stays: still not Playing.
        for (int i = 0; i < 3; i++)
        {
            ticks.RaiseTick();
            Assert.False(wait.IsCompleted, $"completed after tick {i + 1}, before the state turned Playing");
        }

        ticks.RaiseTick();
        await wait.WaitAsync(Settle);
        Assert.Empty(states);
    }

    [Fact]
    public async Task WaitForPlayingAsync_Should_WaitForSomethingToWatch_When_ThePollReturnsNullAtFirst()
    {
        var ticks = new TickSource();
        int polls = 0;

        Task wait = PlayingWatch.WaitForPlayingAsync(
            ticks,
            () => ++polls > 2 ? EngineCompat.ClientStatePlaying : null,
            timeoutTicks: 10);

        ticks.RaiseTick();
        ticks.RaiseTick();
        Assert.False(wait.IsCompleted);

        ticks.RaiseTick();
        await wait.WaitAsync(Settle);
        Assert.Equal(3, polls);
    }

    [Fact]
    public async Task WaitForPlayingAsync_Should_PollOncePerTick_When_Waiting()
    {
        var ticks = new TickSource();
        int polls = 0;
        Task wait = PlayingWatch.WaitForPlayingAsync(
            ticks,
            () =>
            {
                polls++;
                return EnumClientState.Connected;
            },
            timeoutTicks: 4);

        for (int i = 0; i < 4; i++)
        {
            ticks.RaiseTick();
        }

        await Assert.ThrowsAsync<ScenarioTimeoutException>(() => wait.WaitAsync(Settle));
        Assert.Equal(4, polls);
    }

    [Fact]
    public async Task WaitForPlayingAsync_Should_TimeOut_When_TheStateNeverTurnsPlaying()
    {
        var ticks = new TickSource();

        Task wait = PlayingWatch.WaitForPlayingAsync(ticks, () => EnumClientState.Connected, timeoutTicks: 3);
        for (int i = 0; i < 3; i++)
        {
            ticks.RaiseTick();
        }

        ScenarioTimeoutException ex = await Assert.ThrowsAsync<ScenarioTimeoutException>(() => wait.WaitAsync(Settle));
        Assert.Equal(3, ex.TicksWaited);
    }
}
