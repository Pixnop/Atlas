using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Runs one chat command as the engine's own server console and hands back its FINAL
/// result. Single owner of that plumbing for both callers: the author-facing
/// <see cref="WorldSession.ExecuteCommand"/> (which maps the result to a
/// <see cref="Api.CommandResult"/>) and the rollback machinery's internal command use
/// (<c>WorldSnapshot</c>, which only reads the status message).</summary>
internal static class ConsoleCommands
{
    /// <summary>Executes <paramref name="command"/> through the engine's unparsed command
    /// dispatch, as a console caller with the admin role and every privilege - the exact caller
    /// the engine builds for its own server-console commands.</summary>
    /// <param name="api">The live server API owning the command registry.</param>
    /// <param name="command">The slash-prefixed command text.</param>
    /// <returns>A task carrying the command's final result.</returns>
    /// <remarks><para>A command whose argument parsing goes async reports
    /// <c>Deferred</c> first and calls back again with the real outcome once the handler has run;
    /// only that final result is the command's outcome, so the deferred callback is skipped and
    /// the task stays pending. A handler that never calls back leaves it pending forever, which
    /// the scenario watchdog is the bound for.</para>
    /// <para>Continuations run asynchronously: a deferred command's final callback fires from
    /// inside the engine's own command frame, and resuming an awaiting scenario there would run
    /// scenario code re-entrantly inside the engine. The scheduler drain of the next pump pass
    /// resumes it instead. A command that completes synchronously (the common case) hands back
    /// an already-completed task, so its caller still resumes without a tick.</para></remarks>
    public static Task<TextCommandResult> ExecuteAsync(ICoreServerAPI api, string command)
    {
        var tcs = new TaskCompletionSource<TextCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.ChatCommands.ExecuteUnparsed(
            command,
            new TextCommandCallingArgs
            {
                Caller = new Caller
                {
                    Type = EnumCallerType.Console,
                    CallerRole = "admin",
                    CallerPrivileges = ["*"],
                    FromChatGroupId = GlobalConstants.ConsoleGroup,
                },
            },
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
