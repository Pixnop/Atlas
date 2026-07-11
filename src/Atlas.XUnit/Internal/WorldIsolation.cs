namespace Atlas.XUnit.Internal;

/// <summary>How a scenario's world is isolated from earlier scenarios on the same class host,
/// resolved from the <see cref="AtlasScenarioAttribute"/> flags by
/// <see cref="WorldIsolationResolver"/>.</summary>
internal enum WorldIsolation
{
    /// <summary>No isolation requested: the scenario runs against the class host's world as the
    /// previous scenario left it (the default).</summary>
    SharedWorld,

    /// <summary>Full host recycle before the scenario: fresh engine statics, mods and world.
    /// The strongest and slowest form (<see cref="AtlasScenarioAttribute.FreshWorld"/>).</summary>
    FreshWorld,

    /// <summary>World rollback before the scenario: the class host's world is restored to its
    /// snapshot without a reboot (<see cref="AtlasScenarioAttribute.RollbackWorld"/>), falling
    /// back to a full recycle if the rollback fails.</summary>
    RollbackWorld,

    /// <summary>Genuine server restart before the scenario: the class host is shut down
    /// gracefully (its shutdown persists the world save) and a replacement host boots against
    /// that persisted save, so the world carries over across a real save/load round trip
    /// (<see cref="AtlasScenarioAttribute.RestartWorld"/>). Works or fails hard: no fallback.</summary>
    RestartWorld,
}
