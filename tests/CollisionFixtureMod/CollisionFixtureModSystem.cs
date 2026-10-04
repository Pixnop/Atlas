using System.Collections.Concurrent;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CollisionFixtureMod;

/// <summary>Registers the <c>AtlasCollisionProbe</c> block class the fixture's two blocks
/// (<c>collisionfixture:solid</c>, a full cube, and <c>collisionfixture:ghost</c>, no collision box
/// and a thin selection box, the shape a pressure plate or a zone block has) are built on, and
/// the log those calls go to. The log is a <see cref="ConcurrentQueue{T}"/> of strings in the
/// server's <c>ObjectCache</c> under <see cref="EventsKey"/>: a type the test assembly shares
/// with the engine's own copy of the mod, so a test reads it without referencing this
/// assembly.</summary>
public sealed class CollisionFixtureModSystem : ModSystem
{
    /// <summary>The <c>ICoreAPI.ObjectCache</c> key of the event log.</summary>
    public const string EventsKey = "collisionfixture:events";

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("AtlasCollisionProbe", typeof(AtlasCollisionProbe));
        api.ObjectCache[EventsKey] = new ConcurrentQueue<string>();
    }
}
