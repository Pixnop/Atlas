using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One chat line the server sent to a test player (<c>SendMessage</c>, group
/// broadcasts, join announcements, command replies), decoded the way a client's
/// <c>HandleChatLine</c> does.</summary>
/// <param name="Message">The raw message text, exactly as the server sent it.</param>
/// <param name="Type">The chat type the server tagged the line with (<c>OwnMessage</c>,
/// <c>OthersMessage</c>, <c>Notification</c>, <c>JoinLeave</c> for a join or leave
/// announcement, and so on). Read straight off the packet's <c>ChatType</c> field the same way
/// the client casts it; not resolved through <c>EngineCompat</c>, because <c>EnumChatType</c> is
/// a public enum with a stable member order across every supported engine version, unlike the
/// shapes that class exists for.</param>
/// <param name="GroupId">The chat group the line was sent to (<c>GlobalConstants.GeneralChatGroup</c>
/// for a broadcast to everyone, a player group's own id for a group message).</param>
public sealed record ReceivedChatLine(string Message, EnumChatType Type, int GroupId);
