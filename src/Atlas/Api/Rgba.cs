namespace Atlas.Api;

/// <summary>A color decoded from one of the engine's packed <c>int</c> layouts into its four
/// channels (0 to 255 each), so a scenario can assert on <c>.R</c> without knowing which
/// layout the packet kind uses.</summary>
/// <remarks>The engine packs colors two ways, and its two effect systems do not agree (verified
/// by decompile on 1.21.7, 1.22.0 and 1.22.7): block highlights are written verbatim into the
/// mesh's RGBA vertex bytes, so red is the LOWEST byte (<c>ColorUtil.ColorFromRgba(r, g, b, a)</c>,
/// see <see cref="FromRgba"/>), while particles reach the shader as (blue, green, red, alpha)
/// from the low bytes, so red is bits 16 to 23 (<c>ColorUtil.ToRgba(a, r, g, b)</c>, see
/// <see cref="FromArgb"/>). <see cref="HighlightedBlock.Rgba"/> and
/// <see cref="SpawnedParticles.Rgba"/> each apply the layout their packet kind renders with.</remarks>
/// <param name="R">The red channel, 0 to 255.</param>
/// <param name="G">The green channel, 0 to 255.</param>
/// <param name="B">The blue channel, 0 to 255.</param>
/// <param name="A">The alpha channel, 0 to 255.</param>
public readonly record struct Rgba(int R, int G, int B, int A)
{
    /// <summary>Decodes the <c>ColorUtil.ColorFromRgba(r, g, b, a)</c> layout: red in the lowest
    /// byte, alpha in the highest. The layout block highlights render with.</summary>
    /// <param name="packed">The packed color.</param>
    /// <returns>The decoded channels.</returns>
    public static Rgba FromRgba(int packed)
        => new(packed & 0xFF, (packed >> 8) & 0xFF, (packed >> 16) & 0xFF, (packed >> 24) & 0xFF);

    /// <summary>Decodes the <c>ColorUtil.ToRgba(a, r, g, b)</c> layout: blue in the lowest byte,
    /// red in bits 16 to 23, alpha in the highest. The layout particles render with.</summary>
    /// <param name="packed">The packed color.</param>
    /// <returns>The decoded channels.</returns>
    public static Rgba FromArgb(int packed)
        => new((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF, (packed >> 24) & 0xFF);
}
