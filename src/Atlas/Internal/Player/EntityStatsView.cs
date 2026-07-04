using Atlas.Api;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace Atlas.Internal.Player;

/// <summary>Reads common stats off an entity's watched-attribute tree.</summary>
/// <remarks>The <c>health</c>/<c>hunger</c> behaviors (<c>EntityBehaviorHealth</c>,
/// <c>EntityBehaviorHunger</c>) live in the game's own content assembly
/// (<c>VSEssentials.dll</c>), not in <c>VintagestoryAPI</c>/<c>VintagestoryLib</c>, so Atlas
/// cannot reference their types directly. Both behaviors keep their state in
/// <see cref="Entity.WatchedAttributes"/> under a named sub-tree
/// (<c>"health"</c>/<c>"hunger"</c>) with well-known keys, verified by inspecting the
/// shipped assembly: <c>health/currenthealth</c>, <c>health/maxhealth</c>,
/// <c>hunger/currentsaturation</c>. Reading through the tree avoids the extra reference and
/// works whether or not the behavior assembly happens to be loaded, since it is purely a data
/// read with no method call onto the behavior itself.</remarks>
internal sealed class EntityStatsView : IEntityStats
{
    private readonly Entity _entity;

    /// <summary>Initializes a new instance of the <see cref="EntityStatsView"/> class.</summary>
    /// <param name="entity">The entity to read stats from.</param>
    public EntityStatsView(Entity entity) => _entity = entity;

    /// <inheritdoc/>
    public float Health => _entity.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth") ?? 0f;

    /// <inheritdoc/>
    public float MaxHealth => _entity.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("maxhealth") ?? 0f;

    /// <inheritdoc/>
    public float Saturation => _entity.WatchedAttributes.GetTreeAttribute("hunger")?.GetFloat("currentsaturation") ?? 0f;

    /// <inheritdoc/>
    public T? Attribute<T>(string path)
    {
        IAttribute? attribute = _entity.WatchedAttributes.GetAttributeByPath(path);
        return attribute is null ? default : ConvertValue<T>(attribute.GetValue());
    }

    /// <summary>Converts an <see cref="IAttribute"/>'s raw value to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The requested value type.</typeparam>
    /// <param name="value">The raw value returned by <see cref="IAttribute.GetValue"/>.</param>
    /// <returns>The converted value, or <see langword="default"/> if <paramref name="value"/> is
    /// <see langword="null"/> or not convertible to <typeparamref name="T"/>.</returns>
    private static T? ConvertValue<T>(object? value)
    {
        if (value is T typed)
        {
            return typed;
        }

        if (value is null)
        {
            return default;
        }

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return default;
        }
    }
}
