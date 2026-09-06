using System.Globalization;

namespace Atlas.Api;

/// <summary>World configuration for a scenario class. Defaults are deterministic and fast.</summary>
/// <remarks>A scenario class does not build this record itself: it declares
/// <c>[AtlasWorld(Seed = ..., WorldType = ..., PlayStyle = ..., SaveFile = ...)]</c> and the
/// xUnit adapter maps the attribute onto these four properties. <see cref="WorldName"/> has no
/// attribute counterpart, so for scenario classes it is always <c>"Atlas"</c>.</remarks>
public sealed record WorldOptions
{
    /// <summary>The default world seed. Internal, and the single owner of the value:
    /// <c>AtlasWorldAttribute</c> initialises its own <c>Seed</c> from it, so a scenario class
    /// that declares no attribute and one that declares an empty one cannot disagree. A const
    /// is inlined at its use site, so this owns the value without growing the public
    /// surface.</summary>
    internal const int DefaultSeed = 424242;

    /// <summary>The default play style; see <see cref="DefaultSeed"/> for why it lives here.</summary>
    internal const string DefaultPlayStyle = "creativebuilding";

    /// <summary>The default world type; see <see cref="DefaultSeed"/> for why it lives here.</summary>
    internal const string DefaultWorldType = "superflat";

    /// <summary>World seed; identical seeds produce identical worlds.</summary>
    public string Seed { get; init; } = DefaultSeed.ToString(CultureInfo.InvariantCulture);

    /// <summary>Play style for the world.</summary>
    public string PlayStyle { get; init; } = DefaultPlayStyle;

    /// <summary>Type of world to create.</summary>
    public string WorldType { get; init; } = DefaultWorldType;

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
