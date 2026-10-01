using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>The outcome of a server command run via <see cref="IWorldSession.ExecuteCommand"/>.</summary>
/// <remarks><para>Wraps the engine's <see cref="TextCommandResult"/> so scenarios can assert on command
/// outcomes directly instead of routing results through side channels (SaveGame data, log
/// scraping) and turning failures into opaque <c>Until</c> timeouts.</para>
/// <para><see cref="Ok"/> is <see cref="EnumCommandStatus.Success"/> and nothing else. A command
/// registered through the engine's legacy <c>RegisterCommand</c> overloads runs its handler and
/// then reports <see cref="EnumCommandStatus.UnknownLegacy"/>, whatever the handler did, so it
/// reads <see cref="Ok"/> as false even though it ran. <see cref="Status"/> is how to tell that
/// case apart from a refusal or a failure.</para></remarks>
/// <param name="Ok">Whether the command completed with <see cref="EnumCommandStatus.Success"/>.</param>
/// <param name="Message">The command's status message, already resolved through the game's
/// localization (the engine stores messages as <c>Lang</c> keys plus parameters). Empty when the
/// command produced no message.</param>
/// <param name="Raw">The engine's raw result. Escape hatch for anything beyond the status, the
/// success flag and the message: <see cref="TextCommandResult.ErrorCode"/>,
/// <see cref="TextCommandResult.Data"/>, the unresolved message and its parameters.</param>
public sealed record CommandResult(bool Ok, string Message, TextCommandResult Raw)
{
    /// <summary>The engine's final status for the command, the same value as
    /// <c>Raw.Status</c>: <see cref="EnumCommandStatus.Success"/>,
    /// <see cref="EnumCommandStatus.Error"/> (the handler or a precondition such as a missing
    /// privilege refused it; <see cref="TextCommandResult.ErrorCode"/> says which),
    /// <see cref="EnumCommandStatus.NoSuchCommand"/> (nothing is registered under that name) or
    /// <see cref="EnumCommandStatus.UnknownLegacy"/>. A legacy command reports
    /// <see cref="EnumCommandStatus.UnknownLegacy"/> after its handler ran, which is why
    /// <see cref="Ok"/> stays false for it. <see cref="EnumCommandStatus.Deferred"/> never shows
    /// here: a command that defers is reported once, with its final result.</summary>
    public EnumCommandStatus Status => Raw.Status;
}
