using Vintagestory.API.Common;

namespace Atlas.Api;

/// <summary>One player group as the server described it to a test player, in a listing
/// (<see cref="ReceivedGroupListing"/>) or on its own (<see cref="ReceivedGroupUpdate"/>). The
/// group's chat history is not kept.</summary>
/// <param name="Uid">The group's id, the same number space as <see cref="ReceivedChatLine.GroupId"/>.</param>
/// <param name="Name">The group's name. Never null.</param>
/// <param name="OwnerUid">The uid of the group's owner. Never null.</param>
/// <param name="Membership">The receiving player's membership in the group, as the packet
/// carried it.</param>
public sealed record ReceivedPlayerGroup(int Uid, string Name, string OwnerUid, EnumPlayerGroupMemberShip Membership);
