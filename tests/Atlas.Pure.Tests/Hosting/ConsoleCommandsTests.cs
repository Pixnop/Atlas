using Atlas.Internal.Hosting;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>Pins the things the console-command plumbing owns that no engine test reaches: a
/// command that reports Deferred first is not taken for an answer, and the awaiter of such a
/// command does not resume inside the engine's own command callback frame. Every command the
/// E2E suite runs completes synchronously, so both paths are unreachable from there without a
/// fixture parser that defers. Also pins the slash-prefix validation shared by
/// <c>WorldSession.ExecuteCommand</c> and <c>TestPlayer.ExecuteCommand</c>.</summary>
public class ConsoleCommandsTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReportOnlyTheFinalResult_When_TheCommandDefers()
    {
        (ICoreServerAPI api, Func<Action<TextCommandResult>> callback) = FakeServer();

        Task<TextCommandResult> pending = ConsoleCommands.ExecuteAsync(api, "/deferring", ConsoleCommands.Console());

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
        Task<TextCommandResult> pending = ConsoleCommands.ExecuteAsync(api, "/deferring", ConsoleCommands.Console());
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

    [Theory]
    [InlineData(EnumCommandStatus.Success, true)]
    [InlineData(EnumCommandStatus.Error, false)]
    [InlineData(EnumCommandStatus.NoSuchCommand, false)]
    [InlineData(EnumCommandStatus.UnknownLegacy, false)]
    public async Task RunAsync_Should_ExposeTheEnginesStatus_And_ReadOkOnlyForSuccess(EnumCommandStatus status, bool ok)
    {
        (ICoreServerAPI api, Func<Action<TextCommandResult>> callback) = FakeServer();
        Task<CommandResult> pending = ConsoleCommands.RunAsync(api, "/any", ConsoleCommands.Console());

        callback()(new TextCommandResult { Status = status });

        CommandResult result = await pending;
        Assert.Equal(status, result.Status);
        Assert.Equal(ok, result.Ok);
    }

    [Theory]
    [InlineData("time set day")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateSlashPrefixed_Should_Throw_When_TheCommandHasNoLeadingSlash(string? command)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => ConsoleCommands.ValidateSlashPrefixed(command!));
        Assert.Equal("command", ex.ParamName);
    }

    [Fact]
    public void ValidateSlashPrefixed_Should_NotThrow_When_TheCommandHasALeadingSlash()
        => ConsoleCommands.ValidateSlashPrefixed("/time set day");

    [Fact]
    public void Console_Should_BuildTheAdminConsoleCaller()
    {
        Caller caller = ConsoleCommands.Console();

        Assert.Equal(EnumCallerType.Console, caller.Type);
        Assert.Equal("admin", caller.CallerRole);
        Assert.Equal(["*"], caller.CallerPrivileges);
        Assert.Equal(GlobalConstants.ConsoleGroup, caller.FromChatGroupId);
        Assert.Null(caller.Player);
    }

    [Fact]
    public void Player_Should_BuildAPlayerCaller_With_NoRoleOrPrivilegeOverride()
    {
        IServerPlayer player = new BarePlayer();

        Caller caller = ConsoleCommands.Player(player);

        // Type flips to Player, and no role/privileges are forced: HasPrivilege falls through to
        // Player.HasPrivilege, the player's real grants.
        Assert.Equal(EnumCallerType.Player, caller.Type);
        Assert.Same(player, caller.Player);
        Assert.Null(caller.CallerRole);
        Assert.Null(caller.CallerPrivileges);
        Assert.Equal(GlobalConstants.GeneralChatGroup, caller.FromChatGroupId);
    }

    /// <summary>A server api whose command dispatch runs no command and records the completion
    /// callback instead, so a test can drive the deferred-then-final sequence by hand.</summary>
    private static (ICoreServerAPI Api, Func<Action<TextCommandResult>> Callback) FakeServer()
    {
        Action<TextCommandResult>? captured = null;
        IChatCommandApi chat = Substitute.For<IChatCommandApi>();
        chat.When(c => c.ExecuteUnparsed(
                Arg.Any<string>(),
                Arg.Any<TextCommandCallingArgs>(),
                Arg.Any<Action<TextCommandResult>>()))
            .Do(call => captured = call.Arg<Action<TextCommandResult>>());

        ICoreServerAPI api = Substitute.For<ICoreServerAPI>();
        api.ChatCommands.Returns(chat);
        return (api, () => captured ?? throw new InvalidOperationException("no command was dispatched"));
    }

    /// <summary>The engine's own player class with the world wiring of its constructor skipped.
    /// Not a substitute: from 1.22.4 <c>IPlayer</c> declares an internal member, which a generated
    /// proxy cannot implement, so <c>Substitute.For&lt;IServerPlayer&gt;()</c> throws a
    /// <see cref="TypeLoadException"/> whenever the suite compiles against 1.22.4 or later. The
    /// real class implements the interface on every install, and has no entity here.</summary>
    private sealed class BarePlayer() : ServerPlayer(null!, new ServerWorldPlayerData())
    {
        protected override void Init()
        {
        }
    }
}
