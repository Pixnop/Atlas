namespace Atlas.Api;

/// <summary>World configuration for a scenario class. Defaults are deterministic and fast.</summary>
public sealed record WorldOptions
{
    /// <summary>World seed; identical seeds produce identical worlds.</summary>
    public string Seed { get; init; } = "424242";

    /// <summary>Play style for the world.</summary>
    public string PlayStyle { get; init; } = "creativebuilding";

    /// <summary>Type of world to create.</summary>
    public string WorldType { get; init; } = "superflat";

    /// <summary>Name of the world.</summary>
    public string WorldName { get; init; } = "Atlas";
}
