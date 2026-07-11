using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Atlas.Internal.Rollback;

/// <summary>Everything <see cref="WorldSnapshot"/> captures for one joined test player, plus the
/// logic to reset the LIVE connected player back to it (spec stage 2, player-aware rollback).</summary>
/// <remarks><para>Two representations are captured deliberately. The database blob (the
/// <see cref="ServerWorldPlayerData"/> protobuf the forced save just wrote through
/// <c>GameDatabase.SetPlayerData</c>) is written back verbatim at restore, so the persistent
/// image matches the snapshot even if a mid-scenario save overwrote it, and it doubles as the
/// source of the world-player-data scalars and per-player moddata (its public surface). The live
/// pieces (watched attributes, plain attributes, position, inventory trees) are captured
/// separately with the engine's own public serialization primitives, because restored blobs
/// alone do not update in-memory state: the blob's serialized inventories and entity live in
/// private fields whose inflation path (<c>ServerWorldPlayerData.Init</c>) builds NEW instances,
/// while a live reset must mutate the EXISTING ones that the connected client, the inventory
/// manager and mod behaviors already hold references to.</para>
/// <para>The capture mirrors what <c>ServerWorldPlayerData.BeforeSerialization</c> persists
/// (verified by decompile on 1.22.3): per-inventory <c>ToTreeAttributes</c> for every
/// <see cref="InventoryBasePlayer"/>, and the entity's watched attributes with
/// <c>Entity.Stats</c> flushed into them first, exactly like <c>Entity.ToBytes</c> does. The
/// reset applies watched and plain attributes with <see cref="TreeRestore.ApplyInPlace"/>
/// rather than <c>FromBytes</c>, preserving sub-tree object identity for behaviors that cached
/// tree references (e.g. the hunger behavior). Not reset, documented boundary: animation state,
/// controls and other client-driven interaction state (test players are headless), and the
/// host-scoped <c>ServerPlayerData</c> (privileges, role, playerdata.json), which is not world
/// state.</para>
/// <para>Every member runs on the game thread.</para></remarks>
internal sealed class PlayerRollbackState
{
    private readonly byte[] _databaseBlob;
    private readonly byte[] _watchedAttributesBytes;
    private readonly byte[] _attributesBytes;
    private readonly byte[] _positionBytes;
    private readonly Dictionary<string, byte[]> _inventoryTrees;

    private PlayerRollbackState(
        string playerUid,
        byte[] databaseBlob,
        byte[] watchedAttributesBytes,
        byte[] attributesBytes,
        byte[] positionBytes,
        Dictionary<string, byte[]> inventoryTrees)
    {
        PlayerUid = playerUid;
        _databaseBlob = databaseBlob;
        _watchedAttributesBytes = watchedAttributesBytes;
        _attributesBytes = attributesBytes;
        _positionBytes = positionBytes;
        _inventoryTrees = inventoryTrees;
    }

    /// <summary>Gets the captured player's UID, the identity every restore keys on.</summary>
    public string PlayerUid { get; }

    /// <summary>Gets the captured <see cref="ServerWorldPlayerData"/> protobuf, as the forced
    /// save wrote it to the database.</summary>
    public byte[] DatabaseBlob => _databaseBlob;

    /// <summary>Captures one joined player's rollback baseline.</summary>
    /// <param name="client">The fully joined client (entity spawned, inventories wired up).</param>
    /// <param name="database">The open game database; the forced save that just completed wrote
    /// this player's fresh blob, which is read back here.</param>
    /// <returns>The captured state.</returns>
    /// <exception cref="Api.AtlasSetupException">Thrown when the forced save left no database
    /// blob for the player: the save machinery did not behave as the spec's audit found.</exception>
    public static PlayerRollbackState Capture(ConnectedClient client, GameDatabase database)
    {
        string uid = client.Player.PlayerUID;
        byte[] blob = database.GetPlayerData(uid)
            ?? throw new Api.AtlasSetupException(
                $"World rollback: the forced save wrote no playerdata blob for joined test player " +
                $"'{client.PlayerName}' (uid '{uid}'); the save machinery did not behave as expected.");

        EntityPlayer entity = client.Entityplayer;

        // Flush live stats into the watched attributes before serializing them, mirroring
        // Entity.ToBytes (the engine's own persistence path does the same flush first).
        entity.Stats.ToTreeAttributes(entity.WatchedAttributes, forClient: false);

        var inventoryTrees = new Dictionary<string, byte[]>();
        foreach ((string key, InventoryBase inventory) in LiveInventories(client))
        {
            // Same filter as the engine's own BeforeSerialization, plus one refinement: an
            // inventory that serializes to an EMPTY tree holds no restorable state at all (the
            // creative inventory's To/FromTreeAttributes are no-ops; its content is generated,
            // not player-owned), so recording it would only make the restore poke at
            // pseudo-inventories whose slot surface is not safe to touch on a headless player.
            if (inventory is InventoryBasePlayer)
            {
                var tree = new TreeAttribute();
                inventory.ToTreeAttributes(tree);
                if (tree.Count > 0)
                {
                    inventoryTrees[key] = tree.ToBytes();
                }
            }
        }

        using var positionStream = new MemoryStream();
        using (var writer = new BinaryWriter(positionStream))
        {
            entity.Pos.ToBytes(writer);
        }

        return new PlayerRollbackState(
            uid,
            blob,
            entity.WatchedAttributes.ToBytes(),
            entity.Attributes.ToBytes(),
            positionStream.ToArray(),
            inventoryTrees);
    }

    /// <summary>Resets the live connected player to the captured baseline: inventories, watched
    /// attributes (health, saturation, custom mod trees), plain attributes, position, world
    /// player data scalars (game mode, move speed, picking range, spawn, hotbar slot, deaths)
    /// and per-player moddata.</summary>
    /// <param name="client">The live client for <see cref="PlayerUid"/>.</param>
    public void RestoreLive(ConnectedClient client)
    {
        EntityPlayer entity = client.Entityplayer;

        // Inventories: mutate the EXISTING inventory instances (the same objects the inventory
        // manager and the client hold), the way ServerWorldPlayerData.Init inflates them, then
        // mark every slot dirty so listeners observe the reset. Inventories created by a mod
        // after the capture are left alone: they did not exist in the baseline and rolling them
        // back is the owning mod's lifecycle to manage.
        foreach ((string key, InventoryBase inventory) in LiveInventories(client))
        {
            if (_inventoryTrees.TryGetValue(key, out byte[]? bytes))
            {
                var tree = new TreeAttribute();
                tree.FromBytes(bytes);
                inventory.FromTreeAttributes(tree);
                for (int slot = 0; slot < inventory.Count; slot++)
                {
                    inventory.MarkSlotDirty(slot);
                }
            }
        }

        // Watched and plain attributes: in-place reset (see TreeRestore for why not FromBytes),
        // from trees freshly deserialized per restore so no captured object is ever aliased into
        // two resets. Then mirror Entity.FromBytes: re-derive Stats from the restored tree, and
        // mark everything dirty for the (inert, dummy-network) client sync.
        var watchedBaseline = new TreeAttribute();
        watchedBaseline.FromBytes(_watchedAttributesBytes);
        TreeRestore.ApplyInPlace(entity.WatchedAttributes, watchedBaseline);
        entity.Stats.FromTreeAttributes(entity.WatchedAttributes);
        entity.WatchedAttributes.MarkAllDirty();

        var attributesBaseline = new TreeAttribute();
        attributesBaseline.FromBytes(_attributesBytes);
        TreeRestore.ApplyInPlace(entity.Attributes, attributesBaseline);

        // Position: read back into the existing EntityPos instance (on the server, Entity.Pos
        // IS ServerPos: the property forwards). No deferred teleport is needed: restores reset
        // players before the snapshot columns are reloaded, and the captured position lies in
        // those columns by construction (the player was standing there at capture time).
        using (var reader = new BinaryReader(new MemoryStream(_positionBytes)))
        {
            entity.Pos.FromBytes(reader);
        }

        RestoreWorldData(client.WorldData);
        entity.UpdatePartitioning();
    }

    /// <summary>Copies the captured world-player-data scalars and per-player moddata onto the
    /// live <see cref="ServerWorldPlayerData"/> instance, from a fresh deserialization of the
    /// captured database blob (its public surface carries every scalar the engine persists).</summary>
    /// <param name="live">The live world player data to reset.</param>
    private void RestoreWorldData(ServerWorldPlayerData live)
    {
        ServerWorldPlayerData baseline = SerializerUtil.Deserialize<ServerWorldPlayerData>(_databaseBlob);
        live.GameMode = baseline.GameMode;
        live.MoveSpeedMultiplier = baseline.MoveSpeedMultiplier;
        live.PickingRange = baseline.PickingRange;
        live.PreviousPickingRange = baseline.PreviousPickingRange;
        live.FreeMove = baseline.FreeMove;
        live.NoClip = baseline.NoClip;
        live.Deaths = baseline.Deaths;
        live.RenderMetaBlocks = baseline.RenderMetaBlocks;
        live.SelectedHotbarSlot = baseline.SelectedHotbarSlot;
        live.AreaSelectionMode = baseline.AreaSelectionMode;
        live.FreeMovePlaneLock = baseline.FreeMovePlaneLock;
        live.DidSelectSkin = baseline.DidSelectSkin;
        live.SpawnPosition = baseline.SpawnPosition;

        live.ModData.Clear();
        foreach ((string key, byte[] value) in baseline.ModData)
        {
            live.ModData[key] = value;
        }
    }

    /// <summary>Reaches the live inventory instances through the public
    /// <see cref="PlayerInventoryManager.Inventories"/> field: the same
    /// <see cref="InventoryBase"/> objects the player's world data owns (the manager is
    /// constructed over that very dictionary).</summary>
    /// <param name="client">The live client.</param>
    /// <returns>The inventory dictionary entries.</returns>
    private static IEnumerable<KeyValuePair<string, InventoryBase>> LiveInventories(ConnectedClient client)
        => client.Player.InventoryManager is PlayerInventoryManager { Inventories: not null } manager
            ? manager.Inventories
            : [];
}
