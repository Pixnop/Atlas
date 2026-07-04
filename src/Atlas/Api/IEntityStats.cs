using Vintagestory.API.Common.Entities;

namespace Atlas.Api;

/// <summary>Read-only diagnostic view over an entity's common stats, for assertions.</summary>
/// <remarks>Wraps <see cref="Entity.WatchedAttributes"/> and the standard health/hunger
/// behaviors that are otherwise easy to get wrong (wrong tree name, wrong attribute key,
/// forgetting the behavior can be absent on non-living entities). Every member is a thin,
/// null-safe wrapper: nothing here does anything the raw API can't already do; it exists
/// purely so a failing assertion reads "expected health 20 but was 14" instead of a
/// <see cref="NullReferenceException"/> three frames deep in a watched-attribute tree walk.</remarks>
public interface IEntityStats
{
    /// <summary>Gets the entity's current health.</summary>
    /// <remarks>Backed by the <c>health</c> behavior (present on living creatures, including
    /// players). Zero if the entity has no health behavior.</remarks>
    float Health { get; }

    /// <summary>Gets the entity's maximum health.</summary>
    /// <remarks>Backed by the <c>health</c> behavior. Zero if the entity has no health behavior.</remarks>
    float MaxHealth { get; }

    /// <summary>Gets the entity's current saturation (hunger fill level).</summary>
    /// <remarks>Backed by the <c>hunger</c> behavior (present on players and some creatures).
    /// Zero if the entity has no hunger behavior.</remarks>
    float Saturation { get; }

    /// <summary>Reads a typed value from the entity's watched-attribute tree, for stats not
    /// covered by <see cref="Health"/>/<see cref="MaxHealth"/>/<see cref="Saturation"/>
    /// (temporal stability, armor durability, custom mod behaviors' own trees).</summary>
    /// <typeparam name="T">The expected attribute value type, e.g. <see langword="float"/>,
    /// <see langword="int"/>, <see langword="bool"/>, <see langword="string"/>.</typeparam>
    /// <param name="path">The attribute path, e.g. <c>"health/currenthealth"</c> or a
    /// top-level key such as <c>"tempStab"</c>. Use <c>/</c> to descend into a nested tree.</param>
    /// <returns>The attribute value, or <see langword="null"/> if the path does not resolve or
    /// does not convert to <typeparamref name="T"/>.</returns>
    T? Attribute<T>(string path);
}
