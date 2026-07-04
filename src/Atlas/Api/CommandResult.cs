using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>The outcome of a server command run via <see cref="IWorldSession.ExecuteCommand"/>.</summary>
/// <remarks>Wraps the engine's <see cref="TextCommandResult"/> so scenarios can assert on command
/// outcomes directly instead of routing results through side channels (SaveGame data, log
/// scraping) and turning failures into opaque <c>Until</c> timeouts.</remarks>
/// <param name="Ok">Whether the command completed with <see cref="EnumCommandStatus.Success"/>.</param>
/// <param name="Message">The command's status message, already resolved through the game's
/// localization (the engine stores messages as <c>Lang</c> keys plus parameters). Empty when the
/// command produced no message.</param>
/// <param name="Raw">The engine's raw result. Escape hatch for anything beyond the success flag
/// and message: <see cref="TextCommandResult.ErrorCode"/>, <see cref="TextCommandResult.Data"/>,
/// the unresolved message and its parameters.</param>
public sealed record CommandResult(bool Ok, string Message, TextCommandResult Raw);
