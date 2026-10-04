using System.Collections.Concurrent;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CollisionFixtureMod;

/// <summary>A block that appends one line to the event log per call a player makes:
/// <c>collide|code|x,y,z|FACE</c> for <c>OnEntityCollide</c> (the face is the engine's own
/// argument) and <c>inside|code|x,y,z</c> for <c>OnEntityInside</c>. Calls by anything that is
/// not a player (a dropped item landing on the block, say) are not logged.</summary>
public sealed class AtlasCollisionProbe : Block
{
    public override void OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
    {
        Log(entity, $"inside|{Code.Path}|{pos.X},{pos.Y},{pos.Z}");
        base.OnEntityInside(world, entity, pos);
    }

    public override void OnEntityCollide(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, Vec3d collideSpeed, bool isImpact)
    {
        Log(entity, $"collide|{Code.Path}|{pos.X},{pos.Y},{pos.Z}|{facing.Code}");
        base.OnEntityCollide(world, entity, pos, facing, collideSpeed, isImpact);
    }

    private void Log(Entity entity, string line)
    {
        if (entity is EntityPlayer && api.ObjectCache.TryGetValue(CollisionFixtureModSystem.EventsKey, out object? events))
        {
            ((ConcurrentQueue<string>)events).Enqueue(line);
        }
    }
}
