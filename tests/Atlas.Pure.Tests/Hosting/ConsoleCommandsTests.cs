using Atlas.Internal.Hosting;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>Pins the two things the console-command plumbing owns that no engine test reaches:
/// a command that reports Deferred first is not taken for an answer, and the awaiter of such a
/// command does not resume inside the engine's own command callback frame. Every command the
/// E2E suite runs completes synchronously, so both paths are unreachable from there without a
/// fixture parser that defers.</summary>
public class ConsoleCommandsTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReportOnlyTheFinalResult_When_TheCommandDefers()
    {
        (ICoreServerAPI api, Func<Action<TextCommandResult>> callback) = FakeServer();

        Task<TextCommandResult> pending = ConsoleCommands.ExecuteAsync(api, "/deferring");

        callback()(TextCommandResult.Deferred);
        Assert.False(pending.IsCompleted);

        callback()(TextCommandResult.Success("done"));

        TextCommandResult result = await pending;
        Assert.Equal(EnumCommandStatus.Success, result.Status);
        Assert.Equal("done", result.StatusMessage);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ResumeItsAwaiterOffTheCallbackFrame_When_TheCommandDefers()
    {
        (ICoreServerAPI api, Func<Action<TextCommandResult>> callback) = FakeServer();
        Task<TextCommandResult> pending = ConsoleCommands.ExecuteAsync(api, "/deferring");
        using var resumed = new ManualResetEventSlim();
        int resumedOn = 0;
        Task awaiting = Resume();

        callback()(TextCommandResult.Deferred);
        int callbackThread = Environment.CurrentManagedThreadId;
        callback()(TextCommandResult.Success("done"));

        // Blocking rather than awaiting: it keeps this thread busy, so the awaiter can only have
        // run on another one. Had the completion source resumed continuations inline, the awaiter
        // would have run here, inside the callback, on this thread.
        Assert.True(resumed.Wait(TimeSpan.FromSeconds(30)));
        Assert.NotEqual(callbackThread, resumedOn);
        await awaiting;

        async Task Resume()
        {
            await pending;
            resumedOn = Environment.CurrentManagedThreadId;
            resumed.Set();
        }
    }

    /// <summary>A server api whose command dispatch runs no command and records the completion
    /// callback instead, so a test can drive the deferred-then-final sequence by hand.</summary>
    private static (ICoreServerAPI Api, Func<Action<TextCommandResult>> Callback) FakeServer()
    {
        Action<TextCommandResult>? captured = null;
        var chat = Substitute.For<IChatCommandApi>();
        chat.When(c => c.ExecuteUnparsed(
                Arg.Any<string>(),
                Arg.Any<TextCommandCallingArgs>(),
                Arg.Any<Action<TextCommandResult>>()))
            .Do(call => captured = call.Arg<Action<TextCommandResult>>());

        var api = Substitute.For<ICoreServerAPI>();
        api.ChatCommands.Returns(chat);
        return (api, () => captured ?? throw new InvalidOperationException("no command was dispatched"));
    }
}
