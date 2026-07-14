using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Atlas.Internal.Bootstrap;
using Vintagestory.Server;

namespace Atlas.Internal.Hosting;

/// <summary>Thin reflective shell over the completion signal of the engine's background
/// server-assets build, the single owner of that reflection for both ends of the host
/// lifecycle: the dispose-side wait (<c>ServerHost.WaitForAssetsBuildToSettle</c>) and the
/// post-join guard (<c>WorldSession.JoinPlayer</c>, issue #84).</summary>
/// <remarks><para>The engine flow being probed (verified by decompile, identical on 1.20.12,
/// 1.21.7 and 1.22.3): <c>ServerMain.Launch()</c> queues <c>BuildServerAssetsPacket</c> on a
/// TyronThreadPool thread at boot (WorldReady phase, unconditionally - join or no join), and
/// the private <c>ServerMain.serverAssetsPacket</c> box publishes its completion: non-dedicated
/// builds (the Atlas case) assign the box's <c>packet</c> field, dedicated ones serialize into
/// the box and bump its <c>Length</c>. That is the exact state the engine's own
/// <c>WaitOnBuildServerAssetsPacket</c> polls while a joining player's <c>HandleRequestJoin</c>
/// (packet 11) waits on the build inside <c>SendServerAssets</c>.</para>
/// <para>The box is initialized inline at construction, so probing is safe on any server
/// object, even one whose boot crashed before <c>Launch()</c> queued the build (in that rare
/// case the signal never fires and the caller's bounded wait is the way out). The
/// <see cref="FieldInfo"/>s are cached process-wide: the engine's field layout is a
/// per-game-version fact and hosts recycle many times per suite, so after the first resolution
/// a settled probe costs two cached-reflection reads. The signal-shape decision and the field
/// resolution rules live in the pure <see cref="AssetsBuildSignal"/>; this shell only holds the
/// live box.</para></remarks>
internal sealed class ServerAssetsBuildProbe
{
    private static readonly Lazy<FieldInfo?> BoxField = new(() => typeof(ServerMain).GetField(
        "serverAssetsPacket", BindingFlags.NonPublic | BindingFlags.Instance));

    /// <summary>One-time latch for <see cref="WarnProbeMissingOnce"/>.</summary>
    private static int probeMissingWarned;

    /// <summary>Cached packet/Length fields, resolved once per process (every host's box has
    /// the same runtime type). Stays <see langword="null"/> on a drifted layout, in which case
    /// every <see cref="TryCreate"/> degrades to <see langword="null"/> and the caller skips
    /// its wait behind <see cref="WarnProbeMissingOnce"/>.</summary>
    private static (FieldInfo Packet, FieldInfo Length)? boxFields;

    private readonly object _box;

    private ServerAssetsBuildProbe(object box) => _box = box;

    /// <summary>Creates a probe over <paramref name="server"/>'s assets-packet box, or returns
    /// <see langword="null"/> when the engine layout drifted (the box field is gone, or its
    /// packet/Length shape changed); callers degrade to skipping their wait.</summary>
    /// <param name="server">The live (or tearing-down) embedded server to probe.</param>
    /// <returns>The probe, or <see langword="null"/> on engine layout drift.</returns>
    [SuppressMessage(
        "Major Code Smell",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "Reads the engine's non-public completion signal for the background assets build; a missing field (engine layout drift) degrades to skipping the waits with a one-time warning instead of failing the run.")]
    public static ServerAssetsBuildProbe? TryCreate(ServerMain server)
    {
        object? box = BoxField.Value?.GetValue(server);
        if (box == null)
        {
            return null;
        }

        boxFields ??= AssetsBuildSignal.ResolveBoxFields(box.GetType());
        return boxFields == null ? null : new ServerAssetsBuildProbe(box);
    }

    /// <summary>Reads the completion signal: one pass of the dispose-side poll, and the whole
    /// fast path of the post-join guard once the build has settled (the signal never resets on
    /// a live server; only <c>ServerMain.Dispose()</c> clears the box, after every wait that
    /// could read it).</summary>
    /// <returns>Whether the background build has settled.</returns>
    public bool IsBuilt()
    {
        (FieldInfo packet, FieldInfo length) = boxFields!.Value;
        return AssetsBuildSignal.IsBuilt(packet.GetValue(_box) != null, (int)length.GetValue(_box)!);
    }

    /// <summary>Logs the engine-layout-drift warning once per process: hosts recycle many times
    /// per suite and the drift is a per-game-version fact, not a per-wait one.</summary>
    public static void WarnProbeMissingOnce()
    {
        if (Interlocked.Exchange(ref probeMissingWarned, 1) == 0)
        {
            Console.Error.WriteLine(
                "[Atlas] engine field 'ServerMain.serverAssetsPacket' (or its packet/Length shape) " +
                $"not found on game version {EngineCompat.ShortGameVersion}; skipping the waits " +
                "for the background server-assets build (dispose-time, and after a player join). " +
                "A host disposed during that build, or a scenario mutating game content while it " +
                "still runs, may crash the test process (see the boot assets-build race fixes).");
        }
    }
}
