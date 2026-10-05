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
/// case apart from a refusal or a failure.</para>
/// <para>Whose text <see cref="Message"/> holds depends on whether the engine gave one, which
/// <c>Raw.StatusMessage</c> says. When that holds text, <see cref="Message"/> is it resolved, for
/// a success and for a failure alike: a handler's own message, or one the engine wrote itself,
/// such as the <c>RequiresPlayer</c> precondition's <c>Caller must be player</c>. When it holds
/// none (null or empty), <see cref="Message"/> is empty for a success, and for any other status Atlas writes a
/// sentence of its own, so that <c>Assert.True(result.Ok, result.Message)</c> still names the
/// failure: <c>Command '/lobby' failed with status 'NoSuchCommand' and error code
/// 'nosuchcommand'.</c>, with the error code clause left out when there is none. Atlas writes
/// it for a command nothing is registered under, for a legacy command (status
/// <see cref="EnumCommandStatus.UnknownLegacy"/>, whose handler did run) and for an
/// <see cref="EnumCommandStatus.Error"/> that carries no message. That sentence repeats the
/// command line you passed, so an <c>Assert.Contains</c> on <see cref="Message"/> can match it
/// by accident: check <see cref="Status"/> and <see cref="ErrorCode"/> to tell a refusal from a
/// failure, and <c>Raw.StatusMessage</c> for the engine's own text.</para>
/// <para>The <c>RequiresPlayer</c> refusal and an error a handler returns without a code have the
/// same <see cref="Status"/> (<see cref="EnumCommandStatus.Error"/>) and an empty
/// <see cref="ErrorCode"/>, and the engine puts no other mark on the refusal: no code, no data,
/// no flag. They differ only in <c>Raw.StatusMessage</c>, which for the refusal is the literal
/// <c>Caller must be player</c> (not a language key, so <see cref="Message"/> is the same text),
/// the same on 1.20.12, 1.21.7, 1.22.3 and 1.22.7. Compare that text to tell them apart. It is
/// the engine's wording, not a promise: a handler can return the same words, and a later game
/// version can change them, so keep the comparison in one helper of your suite. Run the command
/// with <see cref="ITestPlayer.ExecuteCommand"/> to get past the refusal.</para></remarks>
/// <param name="Ok">Whether the command completed with <see cref="EnumCommandStatus.Success"/>.</param>
/// <param name="Message">The command's status message, already resolved through the game's
/// localization (the engine stores messages as <c>Lang</c> keys plus parameters). The engine's
/// text when it gave one, Atlas's own sentence when a command that did not succeed gave none (see
/// the remarks), and empty for a success with no message.</param>
/// <param name="Raw">The engine's raw result. Escape hatch for anything beyond the status, the
/// success flag and the message: <see cref="TextCommandResult.Data"/>, the unresolved message
/// (<see cref="TextCommandResult.StatusMessage"/>, null or empty when the engine gave none) and its
/// parameters.</param>
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

    /// <summary>The engine's error code for a failed command, the same value as
    /// <c>Raw.ErrorCode</c> but never <see langword="null"/>: <c>"nosuchcommand"</c> for a command
    /// nothing is registered under, <c>"noprivilege"</c> for a caller without the privilege, and
    /// whatever code a handler passed to <see cref="TextCommandResult.Error"/>. Empty when the
    /// result carries none, which is every success and any failure that did not name one (the
    /// precondition of a command that requires a player, for one), so <see cref="Status"/> is
    /// what tells those apart from a success. The remarks on this type say how to tell that
    /// precondition from a handler's own error without a code.</summary>
    public string ErrorCode => Raw.ErrorCode ?? string.Empty;
}
