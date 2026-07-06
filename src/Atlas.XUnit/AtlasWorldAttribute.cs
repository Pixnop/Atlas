namespace Atlas.XUnit;

/// <summary>Declares the world configuration a scenario class runs against.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AtlasWorldAttribute : Attribute
{
    /// <summary>Gets or sets the world seed.</summary>
    public int Seed { get; set; } = 424242;

    /// <summary>Gets or sets the type of world to create.</summary>
    public string WorldType { get; set; } = "superflat";

    /// <summary>Gets or sets the play style for the world.</summary>
    public string PlayStyle { get; set; } = "creativebuilding";

    /// <summary>Gets or sets extra mod paths for this class, appended after assembly-level mods.</summary>
    public string[] Mods { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the path to a prebuilt world save (<c>.vcdbs</c>) to load instead of
    /// generating a fresh world, absolute or relative to the test assembly's directory. The
    /// fixture's file name does not matter and the fixture is never written to: each test class
    /// runs against its own pristine copy. When set, <see cref="Seed"/>, <see cref="WorldType"/>
    /// and <see cref="PlayStyle"/> are ignored; the savegame carries its own world configuration.</summary>
    public string? SaveFile { get; set; }
}
