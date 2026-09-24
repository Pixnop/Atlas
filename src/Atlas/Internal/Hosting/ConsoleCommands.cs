using Atlas.Api;
using Atlas.Internal.Bootstrap;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Runs one chat command through the engine's unparsed command dispatch as a given
/// <see cref="Caller"/> and maps its FINAL result onto the author-facing <see cref="CommandResult"/>.
/// Single owner of that plumbing for every caller that goes through it: the console
/// (<see cref="WorldSession.ExecuteCommand"/>), a joined test player
/// (<see cref="ITestPlayer.ExecuteCommand"/>), and the rollback machinery's internal command use
/// (<c>WorldSnapshot</c>, which only reads the status message). The name is historical, from
/// when the console was the only caller; the caller is now a parameter, not a choice this class
/// makes.</summary>
internal static class ConsoleCommands
{
    /// <summary>Validates that <paramref name="command"/> is slash-prefixed, the same rule every
    /// caller enforces.</summary>
    /// <param name="command">The command text to validate.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="command"/> does not start
    /// with a slash: the engine's command dispatch strips the first character unconditionally, so
    /// a slashless command would be silently misparsed instead of failing loudly.</exception>
    /// <remarks>Called synchronously by each public entry point (never from inside this class's
    /// own <c>async</c> methods) so the exception reaches the caller synchronously too, not
    /// wrapped in a faulted task: a slashless command is an author error, not a command
    /// outcome.</remarks>
    public static void ValidateSlashPrefixed(string command)
    {
        if (string.IsNullOrEmpty(command) || command[0] != '/')
        {
            throw new ArgumentException(
                $"Command '{command}' must start with a slash: the engine's command dispatch " +
                "strips the first character unconditionally, so a slashless command would be " +
                "silently misparsed.",
                nameof(command));
        }
    }

    /// <summary>Builds the caller <see cref="WorldSession.ExecuteCommand"/> runs as: the console,
    /// with the admin role and every privilege, the exact caller the engine builds for its own
    /// server-console commands.</summary>
    /// <returns>The console caller.</returns>
    public static Caller Console() => new()
    {
        Type = EnumCallerType.Console,
        CallerRole = "admin",
        CallerPrivileges = ["*"],
        FromChatGroupId = GlobalConstants.ConsoleGroup,
    };

    /// <summary>Builds the caller <see cref="ITestPlayer.ExecuteCommand"/> runs as: the joined
    /// player itself, with no role or privilege override, so <c>RequiresPrivilege</c> and
    /// <c>RequiresPlayer</c> preconditions see this player's real, current role, not an admin
    /// stand-in.</summary>
    /// <param name="player">The player to run as.</param>
    /// <returns>The player caller. Setting <see cref="Caller.Player"/> also sets
    /// <see cref="Caller.Type"/> to <see cref="EnumCallerType.Player"/> and <see cref="Caller.Pos"/>
    /// to <c>player.Entity.Pos</c>, the same shape the engine's own network dispatch builds for a
    /// chat-typed command (<c>ChatCommandApi.Execute(string, IServerPlayer, ...)</c>). Pre-1.22,
    /// <c>Entity.Pos</c> is a separate instance the join path never updates for a headless
    /// player, so it stays at the origin until a teleport touches it, the same trap
    /// <c>TestPlayer.Position</c> reads around; this method overwrites <see cref="Caller.Pos"/>
    /// right after with <see cref="EngineCompat.SidedPosOf"/> instead, so a handler that resolves
    /// a relative argument (<c>~</c>) off <c>args.Caller.Pos</c> resolves it around the player's
    /// real position on every supported version. A handler that reads
    /// <c>args.Caller.Entity.Pos</c> directly still sees the engine's own, still-stale pre-1.22
    /// instance; this fix only covers <see cref="Caller.Pos"/> itself. A player with no spawned
    /// entity yet (the engine's own setter tolerates this too) keeps whatever
    /// <see cref="Caller.Player"/> assigned, since there is nothing sided to read.
    /// <see cref="Vintagestory.API.Config.GlobalConstants.GeneralChatGroup"/> is what a real
    /// client's typed command runs with by default; nothing here reads it back for these calls,
    /// but a command whose handler replies through <c>args.Caller.FromChatGroupId</c> sees the
    /// same channel a real chat message would.</returns>
    public static Caller Player(IServerPlayer player)
    {
        Caller caller = new()
        {
            Player = player,
            FromChatGroupId = GlobalConstants.GeneralChatGroup,
        };

        // The Player setter above already set Pos from Entity.Pos, which is stale pre-1.22 (see
        // the remarks above); overwrite it with the same sided read TestPlayer.Position uses.
        // SidedPosOf dereferences the entity, so this only runs once one actually exists.
        if (player.Entity != null)
        {
            caller.Pos = EngineCompat.SidedPosOf(player.Entity).XYZ;
        }

        return caller;
    }

    /// <summary>Runs <paramref name="command"/> through the engine's unparsed command dispatch as
    /// <paramref name="caller"/>, and maps the engine's FINAL result onto a <see cref="CommandResult"/>.</summary>
    /// <param name="api">The live server API owning the command registry.</param>
    /// <param name="command">The slash-prefixed command text, already validated by the caller
    /// (<see cref="ValidateSlashPrefixed"/>).</param>
    /// <param name="caller">The caller to run as: <see cref="Console"/> or <see cref="Player"/>.</param>
    /// <returns>A task carrying the command's outcome.</returns>
    /// <remarks><para>A command whose argument parsing goes async (e.g. player lookups) reports
    /// <c>Deferred</c> first and calls back again with the real outcome once the handler has run;
    /// only that final result is the command's outcome, so the deferred callback is skipped and
    /// the task stays pending. A handler that never calls back leaves it pending forever, which
    /// the scenario watchdog is the bound for.</para>
    /// <para>Continuations run asynchronously: a deferred command's final callback fires from
    /// inside the engine's own command frame, and resuming an awaiting scenario there would run
    /// scenario code re-entrantly inside the engine. The scheduler drain of the next pump pass
    /// resumes it instead. A command that completes synchronously (the common case) hands back
    /// an already-completed task, so its caller still resumes without a tick.</para>
    /// <para><c>TextCommandCallingArgs.LanguageCode</c> is left unset here on purpose: the
    /// engine's own dispatch (<c>ChatCommandApi.Execute</c>) overwrites it unconditionally from
    /// <c>(Caller.Player as IServerPlayer)?.LanguageCode ?? Lang.CurrentLocale</c> before a
    /// handler ever runs, so setting it here would just be discarded, and for a player caller
    /// that overwrite already resolves to what a real client of this player would send: the
    /// player's own <c>LanguageCode</c>. It also plays no part in the message this method
    /// returns: <see cref="CommandResult.Message"/> is resolved through the static
    /// <see cref="Lang.Get(string, object[])"/> (the server's own configured locale), the same
    /// for every caller, so a player's language does not change it.</para></remarks>
    public static async Task<CommandResult> RunAsync(ICoreServerAPI api, string command, Caller caller)
    {
        TextCommandResult result = await ExecuteAsync(api, command, caller).ConfigureAwait(true);

        bool ok = result.Status == EnumCommandStatus.Success;
        string message = result.StatusMessage == null
            ? string.Empty
            : Lang.Get(result.StatusMessage, result.MessageParams ?? []);

        // Some engine failures (an unknown command, for one) carry only an error code and no
        // message. Synthesize one so a scenario's Assert.True(result.Ok, result.Message) still
        // names the failure instead of printing an empty string.
        if (!ok && message.Length == 0)
        {
            message = $"Command '{command}' failed with status '{result.Status}'" +
                (string.IsNullOrEmpty(result.ErrorCode) ? "." : $" and error code '{result.ErrorCode}'.");
        }

        return new CommandResult(ok, message, result);
    }

    /// <summary>The raw engine dispatch, one level under <see cref="RunAsync"/>: no
    /// <see cref="CommandResult"/> mapping, just the Deferred-then-final plumbing. Internal
    /// (not private) so the pure suite can pin that plumbing (the Deferred callback being
    /// skipped and the awaiter resuming off the callback frame) without a live server. Most
    /// commands the E2E suite runs complete synchronously; <c>PlayerExecuteCommandTests</c>'
    /// deferred case is the one that actually drives this path against a live server.</summary>
    /// <param name="api">The live server API owning the command registry.</param>
    /// <param name="command">The slash-prefixed command text.</param>
    /// <param name="caller">The caller to run as.</param>
    /// <returns>A task carrying the command's FINAL <see cref="TextCommandResult"/>.</returns>
    internal static Task<TextCommandResult> ExecuteAsync(ICoreServerAPI api, string command, Caller caller)
    {
        var tcs = new TaskCompletionSource<TextCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.ChatCommands.ExecuteUnparsed(
            command,
            new TextCommandCallingArgs { Caller = caller },
            result =>
            {
                if (result.Status == EnumCommandStatus.Deferred)
                {
                    return;
                }

                tcs.TrySetResult(result);
            });
        return tcs.Task;
    }
}
