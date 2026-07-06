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

    /// <summary>Path to a prebuilt world save (<c>.vcdbs</c>) to load instead of generating a
    /// fresh world. Absolute, or relative to the same base directory as mod paths (for scenario
    /// classes, the test assembly's directory).</summary>
    /// <remarks>The fixture is copied into the host's scratch data path before the server boots,
    /// so its file name does not matter and the fixture itself is never written to: every test
    /// class gets a pristine copy. When set, <see cref="Seed"/>, <see cref="WorldType"/> and
    /// <see cref="PlayStyle"/> are ignored; the savegame carries its own world configuration.</remarks>
    public string? SaveFile { get; init; }
}
