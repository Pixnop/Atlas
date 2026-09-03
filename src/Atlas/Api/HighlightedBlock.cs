using Vintagestory.API.MathTools;

namespace Atlas.Api;

/// <summary>One block position of a highlight slot, as the server last sent it to a test player
/// (<c>IWorldAccessor.HighlightBlocks</c>).</summary>
/// <param name="Pos">The highlighted position, dimension included.</param>
/// <param name="Color">The packed color exactly as the sender passed it, or 0 when the sender
/// passed no colors (a real client then draws its own default highlight color). Colors are
/// assigned per position the way the client's highlight renderer does: one color per position
/// when at least as many colors as positions were sent (and more than one), otherwise the first
/// color for every position.</param>
public sealed record HighlightedBlock(BlockPos Pos, int Color)
{
    /// <summary>Gets <see cref="Color"/> decoded with the layout block highlights render with
    /// (<see cref="Rgba.FromRgba"/>: red in the lowest byte).</summary>
    public Rgba Rgba => Rgba.FromRgba(Color);
}
