using Atlas.Internal.Bootstrap;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Lets a real game client past the character creation dialog the survival mod puts in
/// front of every account that has never created a character.</summary>
/// <remarks><para>Why a real client stops there. The survival mod's server side reads the
/// player's <c>createCharacter</c> mod data in its own <c>PlayerJoin</c> handler
/// (<c>CharacterSystem.Event_PlayerJoinServer</c>) and sends the answer to the client on its
/// <c>charselection</c> channel. The client's own <c>PlayerJoin</c> handler opens the character
/// dialog when that answer is false, and its <c>IsPlayerReady</c> handler withholds packet 29
/// (the transition to <c>Playing</c>) until a character was chosen. A fresh embedded world has
/// no mod data, so a real client sits on the dialog for good, never reaching <c>Playing</c>.
/// Identical code on 1.20.12, 1.21.7, 1.22.3 and 1.22.7.</para>
/// <para>Why <see cref="LetPast"/> has to run inside <c>PlayerJoin</c> and ahead of the survival
/// mod. <c>PlayerCreate</c> is raised at the end of <c>ServerMain.HandleRequestJoin</c>, after
/// <c>PlayerJoin</c>, so it is too late. The engine raises the mod-level <c>PlayerJoin</c>
/// handlers in registration order, so this one is registered from the bridge's earliest
/// pre-system (<c>BridgeRendezvous.RegisterEarlyJoin</c>), ahead of every mod's own
/// registration.</para>
/// <para>Real connections only. A test player rides a dummy connection and keeps the engine's
/// own flow untouched: it gets the survival mod's default class, which its tests may already
/// depend on. The check is the engine's own flag for a dummy connection
/// (<see cref="EngineCompat.IsDummyConnection"/>).</para>
/// <para>What the player then lacks. The survival mod skips <c>setCharacterClass</c> when the
/// flag is true, so the player has no <c>characterClass</c> in its watched attributes and keeps
/// the default skin. It also gets none of the trait stat changes a class applies
/// (<c>applyTraitAttributes</c> only runs for a chosen class). Mods that read the class see
/// none, and the survival mod's own recipe trait check lets a player with no class craft
/// everything. A later change may want to pick a class here too.</para></remarks>
internal static class CharacterGate
{
    /// <summary>The mod data key the survival mod reads (a serialized <see cref="bool"/>).</summary>
    internal const string ModDataKey = "createCharacter";

    /// <summary>Tells whether the gate applies to a connection: the server knows it, and it is not
    /// one of the dummy connections test players use.</summary>
    /// <param name="clients">The live server's client table (<c>ServerMain.Clients</c>).</param>
    /// <param name="clientId">The joining player's client id.</param>
    /// <returns><see langword="true"/> for a real connection.</returns>
    internal static bool AppliesTo(CachingConcurrentDictionary<int, ConnectedClient> clients, int clientId)
        => clients.TryGetValue(clientId, out ConnectedClient? client) && !EngineCompat.IsDummyConnection(client);

    /// <summary>Marks the character of a joining real client as already created.</summary>
    /// <param name="clients">The live server's client table (<c>ServerMain.Clients</c>), to look
    /// the player's connection up.</param>
    /// <param name="player">The joining player.</param>
    /// <returns><see langword="true"/> when the player is a real connection and was let past;
    /// <see langword="false"/> for a test player, or a player the server no longer knows.</returns>
    /// <remarks>Runs on the game thread, from <c>TriggerPlayerJoin</c>.</remarks>
    internal static bool LetPast(CachingConcurrentDictionary<int, ConnectedClient> clients, IServerPlayer player)
    {
        if (!AppliesTo(clients, player.ClientId))
        {
            return false;
        }

        player.SetModdata(ModDataKey, SerializerUtil.Serialize(true));
        return true;
    }
}
