using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Atlas.Api;

/// <summary>One particle spawn the server sent to a test player (<c>IWorldAccessor.SpawnParticles</c>),
/// decoded with the engine's own particle-provider codec.</summary>
/// <param name="ProviderClassName">The provider's registered class name (<c>"simple"</c> for the
/// <c>SpawnParticles(quantity, color, minPos, maxPos, ...)</c> overloads, which build a
/// <see cref="SimpleParticleProperties"/>).</param>
/// <param name="Provider">The provider rebuilt from the packet, exactly as a client would
/// rebuild it. Escape hatch for every property not lifted below (life length, gravity, size,
/// model, the <c>AddPos</c>/<c>AddVelocity</c>/<c>AddQuantity</c> extents of a simple provider).</param>
/// <param name="Position">The spawn position: the <c>MinPos</c> box origin of a simple provider
/// (deterministic, what the sender passed as <c>minPos</c>), otherwise the provider's own
/// <c>Pos</c>, read once at decode.</param>
/// <param name="Velocity">The spawn velocity: the <c>MinVelocity</c> of a simple provider,
/// otherwise the provider's <c>GetVelocity</c> at <paramref name="Position"/>, read once at decode.</param>
/// <param name="Quantity">The particle count: the <c>MinQuantity</c> of a simple provider (what
/// the sender passed as <c>quantity</c>), otherwise the provider's <c>Quantity</c>, read once.</param>
/// <param name="Color">The packed color exactly as the sender passed it, for a simple provider;
/// 0 for providers whose color a client resolves from block or item textures (block cubes,
/// item stack cubes, the advanced provider), which no server-side decode can know.</param>
public sealed record SpawnedParticles(
    string ProviderClassName,
    IParticlePropertiesProvider Provider,
    Vec3d Position,
    Vec3f Velocity,
    float Quantity,
    int Color)
{
    /// <summary>Gets <see cref="Color"/> decoded with the layout particles render with
    /// (<see cref="Rgba.FromArgb"/>: red in bits 16 to 23).</summary>
    public Rgba Rgba => Rgba.FromArgb(Color);
}
