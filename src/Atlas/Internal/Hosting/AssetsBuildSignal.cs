using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Atlas.Internal.Hosting;

/// <summary>The pure core of the engine's server-assets-build completion signal, shared by the
/// two guards that wait on it: the dispose-side wait (<c>ServerHost.WaitForAssetsBuildToSettle</c>,
/// the boot assets-build teardown race) and the post-join guard (<c>WorldSession.JoinPlayer</c>,
/// issue #84). Owns every part of the signal that needs no live server: which box fields carry
/// it (<see cref="ResolveBoxFields"/>), when it counts as settled (<see cref="IsBuilt"/>), and
/// how a post-join timeout is worded (<see cref="DescribeJoinTimeout"/>) - so all three are
/// testable without booting a server (the <see cref="AssetsBuildSettle"/> /
/// <see cref="EngineStopDetection"/> pattern). The live reflective reads against a running
/// <c>ServerMain</c> stay in the thin shell, <see cref="ServerAssetsBuildProbe"/>.</summary>
internal static class AssetsBuildSignal
{
    /// <summary>The completion signal, from its raw parts: settled once the box's packet
    /// reference is assigned (how a non-dedicated build - the Atlas case - publishes its result)
    /// or its serialized length is non-zero (how a dedicated build publishes, and what a joining
    /// client's send path bumps when it serializes the built packet). Single owner of the signal
    /// shape; both guards feed it their reflective reads. Same state the engine's own
    /// <c>WaitOnBuildServerAssetsPacket</c> polls, identical on 1.20.12, 1.21.7 and 1.22.3.</summary>
    /// <param name="packetAssigned">Whether the box's <c>packet</c> field is non-null.</param>
    /// <param name="serializedLength">The box's <c>Length</c> field.</param>
    /// <returns>Whether the background build has settled.</returns>
    public static bool IsBuilt(bool packetAssigned, int serializedLength)
        => packetAssigned || serializedLength != 0;

    /// <summary>Resolves the two completion-signal fields on the engine's
    /// <c>serverAssetsPacket</c> box type: the internal <c>packet</c> reference (declared on
    /// <c>BoxedPacket_ServerAssets</c> itself) and the public <c>Length</c> counter (declared on
    /// the <c>BoxedPacket</c> base, which <see cref="Type.GetField(string, BindingFlags)"/>
    /// still returns for a public inherited field). Type-parameterized so the resolution rules
    /// are testable against fake box shapes without booting a server (the
    /// <c>EngineCompat.ResolveExitStateField</c> pattern).</summary>
    /// <param name="boxType">The runtime type of the box instance.</param>
    /// <returns>The resolved fields, or <see langword="null"/> when the engine layout drifted:
    /// either field missing, or <c>Length</c> no longer an <see cref="int"/> (which would make
    /// the read throw instead of settle). Callers degrade to skipping their wait.</returns>
    [SuppressMessage(
        "Major Code Smell",
        "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields",
        Justification = "Resolves the engine's non-public completion signal for the background assets build; a missing field (engine layout drift) degrades to skipping the waits with a one-time warning instead of failing the run.")]
    public static (FieldInfo Packet, FieldInfo Length)? ResolveBoxFields(Type boxType)
    {
        ArgumentNullException.ThrowIfNull(boxType);
        FieldInfo? packet = boxType.GetField("packet", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo? length = boxType.GetField("Length", BindingFlags.Public | BindingFlags.Instance);
        return packet == null || length == null || length.FieldType != typeof(int)
            ? null
            : (packet, length);
    }

    /// <summary>Words the failure of the post-join wait, in the join path's engine-drift voice
    /// (<c>WorldSession.WaitForPlaying</c>): every supported engine queues the build at boot and
    /// settles it well inside the generous bound, so an expiry means the build or its signal
    /// drifted, not that the caller should wait longer.</summary>
    /// <param name="playerName">The player whose join waited on the build.</param>
    /// <param name="timeoutTicks">The tick bound that expired.</param>
    /// <param name="dataPath">The host's scratch data path, containing the server logs.</param>
    /// <returns>The exception message.</returns>
    public static string DescribeJoinTimeout(string playerName, int timeoutTicks, string dataPath)
        => $"Test player '{playerName}' joined, but the engine's background server-assets build " +
           "(queued at boot on the engine's thread pool; the first join's own handling waits on " +
           $"the same signal) never signaled completion within the tick bound ({timeoutTicks} " +
           "ticks). Every supported engine settles that build in seconds, so its completion " +
           "signal (ServerMain.serverAssetsPacket) has likely drifted relative to the Atlas " +
           $"build; check the server logs under '{dataPath}'.";
}
