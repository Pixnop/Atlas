using Vintagestory.API.Datastructures;

namespace Atlas.Internal.Rollback;

/// <summary>Pure helper that resets a live attribute tree to a captured baseline IN PLACE: keys
/// the baseline lacks are removed, keys it carries are copied over, and a sub-tree present on
/// both sides is recursed into instead of being replaced, so the live sub-tree OBJECT survives
/// the reset. That identity preservation is the whole point: engine and mod behaviors cache
/// sub-tree references at initialization (verified on <c>EntityBehaviorHunger</c>, which stores
/// its <c>hungerTree</c> field once in <c>Initialize</c>), so a wholesale
/// <c>TreeAttribute.FromBytes</c> over a live entity's watched attributes would leave those
/// behaviors writing to detached trees. Used by the player-aware world rollback (spec stage 2)
/// to reset a live player's watched attributes and plain attributes.</summary>
internal static class TreeRestore
{
    /// <summary>Resets <paramref name="live"/> to the state of <paramref name="baseline"/>,
    /// preserving the identity of every sub-tree that exists on both sides.</summary>
    /// <param name="live">The live tree to reset in place.</param>
    /// <param name="baseline">The captured baseline. Leaf attributes (and sub-trees absent from
    /// <paramref name="live"/>) are adopted by reference, so callers must pass a tree they will
    /// not reuse: deserialize a fresh instance from the captured bytes for every reset.</param>
    public static void ApplyInPlace(ITreeAttribute live, ITreeAttribute baseline)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(baseline);

        List<string> extraKeys = [.. live
            .Select(pair => pair.Key)
            .Where(key => !baseline.HasAttribute(key))];
        foreach (string key in extraKeys)
        {
            live.RemoveAttribute(key);
        }

        foreach ((string key, IAttribute baselineValue) in baseline)
        {
            if (baselineValue is ITreeAttribute baselineTree && live[key] is ITreeAttribute liveTree)
            {
                ApplyInPlace(liveTree, baselineTree);
            }
            else
            {
                live[key] = baselineValue;
            }
        }
    }
}
