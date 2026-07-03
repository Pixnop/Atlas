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
}
