using Atlas.Internal.Bootstrap;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Atlas.Internal.Player;

/// <summary>Registers an entity Atlas has just moved in the chunk its position is in, right
/// away. The engine does it only once a second, in <c>ServerSystemEntitySimulation.UpdateEvery1000ms</c>
/// (up to 33 passes later), and until it does, the entity's chunk index
/// (<c>Entity.InChunkIndex3d</c>) and its entry in a chunk's entity list still describe the old
/// position. Everything the engine centres on the entity's chunk reads that index: random-tick
/// candidates around a player, for one.</summary>
/// <remarks>Uses <c>IWorldAccessor.UpdateEntityChunk</c>, the public call the engine's own
/// <c>EntityPlayer.ChangeDimension</c> makes and mods make for the same reason, identical on
/// 1.20.12, 1.21.7, 1.22.3 and 1.22.7. It runs on the game thread and costs one chunk lookup, a
/// list removal and an insertion, no wait. It does nothing when the chunk at the position is
/// not loaded (a position above or below the world, a mini-dimension chunk nothing created): the
/// engine has no chunk to register the entity in then, and neither does its own once-a-second
/// pass.</remarks>
internal static class EntityChunk
{
    /// <summary>Registers <paramref name="entity"/> in the chunk of its server-side position.</summary>
    /// <param name="world">The live world accessor, the server's.</param>
    /// <param name="entity">The spawned entity to register.</param>
    /// <param name="onlyWhenIndexDiffers">Whether to leave the entity alone when its chunk index
    /// already matches its position, as the engine's own pass does. A teleport inside one chunk
    /// then does not touch the chunk's entity list. Pass <see langword="false"/> when the chunk
    /// objects themselves were replaced under the entity (a rollback reload), where the matching
    /// index says nothing about the new chunk's list.</param>
    public static void Register(IWorldAccessor world, Entity entity, bool onlyWhenIndexDiffers)
    {
        long index = world.ChunkProvider.ChunkIndex3D(EngineCompat.SidedPosOf(entity));
        if (onlyWhenIndexDiffers && entity.InChunkIndex3d == index)
        {
            return;
        }

        world.UpdateEntityChunk(entity, index);
    }
}
