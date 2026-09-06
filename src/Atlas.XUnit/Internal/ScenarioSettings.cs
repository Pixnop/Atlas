using Xunit.Abstractions;

namespace Atlas.XUnit.Internal;

/// <summary>The per-scenario knobs of <see cref="AtlasScenarioAttribute"/> and
/// <see cref="AtlasTheoryAttribute"/>, carried as one value from discovery, through xUnit's
/// serialization round trip, down to the invoker. Both attributes declare the same five names, so
/// one reader, one writer and one parameter serve the fact and theory halves of the pipeline
/// alike.</summary>
/// <param name="FreshWorld">Whether the scenario recycles the class host before running, giving it
/// a fresh world instead of the one shared by the test class.</param>
/// <param name="RollbackWorld">Whether the scenario rolls the class host's world back to its
/// snapshot before running, the cheap alternative to <paramref name="FreshWorld"/>.</param>
/// <param name="RestartWorld">Whether the scenario restarts the class host before running,
/// carrying the persisted world over onto the replacement host.</param>
/// <param name="StrictIsolation">Whether a degraded rollback fails the scenario instead of
/// silently falling back to a full host recycle.</param>
/// <param name="TimeoutMs">The maximum time, in milliseconds, the scenario is allowed to run
/// before the off-thread watchdog fails it.</param>
/// <remarks><paramref name="TimeoutMs"/> is carried as plain data, NOT mapped onto
/// <see cref="Xunit.Sdk.XunitTestCase.Timeout"/>: see <see cref="AtlasScenarioAttribute.TimeoutMs"/>
/// for why.</remarks>
internal sealed record ScenarioSettings(
    bool FreshWorld,
    bool RollbackWorld,
    bool RestartWorld,
    bool StrictIsolation,
    int TimeoutMs)
{
    /// <summary>Gets the settings of a scenario that asked for nothing: no isolation, no timeout.
    /// The state a test case has between xUnit's parameterless deserialization constructor and
    /// <see cref="Read"/>, matching the field defaults that state used to have.</summary>
    public static ScenarioSettings None { get; } = new(false, false, false, false, 0);

    /// <summary>Reads the five named arguments off a reflected
    /// <see cref="AtlasScenarioAttribute"/> or <see cref="AtlasTheoryAttribute"/>.</summary>
    /// <param name="attribute">The reflected attribute, as the discoverer receives it.</param>
    /// <returns>The scenario's settings.</returns>
    public static ScenarioSettings From(IAttributeInfo attribute) => new(
        attribute.GetNamedArgument<bool>(nameof(AtlasScenarioAttribute.FreshWorld)),
        attribute.GetNamedArgument<bool>(nameof(AtlasScenarioAttribute.RollbackWorld)),
        attribute.GetNamedArgument<bool>(nameof(AtlasScenarioAttribute.RestartWorld)),
        attribute.GetNamedArgument<bool>(nameof(AtlasScenarioAttribute.StrictIsolation)),
        attribute.GetNamedArgument<int>(nameof(AtlasScenarioAttribute.TimeoutMs)));

    /// <summary>Reads the settings back out of a test case's serialized form.</summary>
    /// <param name="data">The serialization info xUnit hands to <c>Deserialize</c>.</param>
    /// <returns>The deserialized settings.</returns>
    public static ScenarioSettings Read(IXunitSerializationInfo data) => new(
        data.GetValue<bool>(nameof(FreshWorld)),
        data.GetValue<bool>(nameof(RollbackWorld)),
        data.GetValue<bool>(nameof(RestartWorld)),
        data.GetValue<bool>(nameof(StrictIsolation)),
        data.GetValue<int>(nameof(TimeoutMs)));

    /// <summary>Writes the settings into a test case's serialized form, one key per knob. A knob
    /// that is not written here silently reverts to its default on the execution side of
    /// `dotnet test`'s discovery/execution round trip.</summary>
    /// <param name="data">The serialization info xUnit hands to <c>Serialize</c>.</param>
    public void Write(IXunitSerializationInfo data)
    {
        data.AddValue(nameof(FreshWorld), FreshWorld);
        data.AddValue(nameof(RollbackWorld), RollbackWorld);
        data.AddValue(nameof(RestartWorld), RestartWorld);
        data.AddValue(nameof(StrictIsolation), StrictIsolation);
        data.AddValue(nameof(TimeoutMs), TimeoutMs);
    }
}
