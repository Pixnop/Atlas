using Vintagestory.API.Datastructures;

namespace Atlas.Internal.Rollback;

/// <summary>The mod cooperation contract of world rollback (stage 3 of the snapshot design,
/// docs/specs/2026-07-11-rollback-stage3-mod-cooperation.md): the event names and versioned
/// <see cref="TreeAttribute"/> payloads <see cref="WorldSnapshot"/> pushes over the engine's
/// event bus. The event name plus the payload shape IS the whole contract: a participating mod
/// references nothing beyond VintagestoryAPI.dll (which it already references by being a mod)
/// and subscribes with <c>api.Event.RegisterEventBusListener(handler, priority,
/// filterByEventName)</c>. No Atlas assembly is ever loaded into the game's mod space, so there
/// is no assembly-identity hazard and no package for shipping mods to depend on.</summary>
/// <remarks><para>The two events, both pushed synchronously on the game thread:</para>
/// <para><c>atlas:rollback:captured</c>: once per capture, immediately after the snapshot is in
/// memory. Payload: <c>version</c> (int, schema version, currently 1), <c>generation</c> (int,
/// increments per capture, process-wide). For mods whose in-memory state is NOT derivable from
/// SaveGame data and that want to pair their own cheap in-memory snapshot with Atlas's. Most
/// mods can ignore this event entirely.</para>
/// <para><c>atlas:rollback:restored</c>: on every restore, after the database blobs and the
/// live <c>SaveGame</c> globals (moddata included) are restored, and BEFORE any chunk column is
/// reloaded, so chunk-loaded handlers and ticks never run against desynced mod state. Payload:
/// <c>version</c> (int), <c>generation</c> (int, matching the capture), <c>restoreCount</c>
/// (int, per generation, starting at 1). The handler's job: rebuild registry-style in-memory
/// state from the now-restored SaveGame, exactly as at boot; the payload deliberately does not
/// hand out the SaveGame instance, because <c>api.WorldManager.SaveGame</c> is already the
/// restored object (the restore mutates the live instance in place).</para>
/// <para>Ordering across mods: bus priority. Library mods that other mods build on subscribe
/// above the 0.5 default (e.g. 0.6) so their state is coherent before their consumers' handlers
/// run, mirroring <c>ExecuteOrder</c> at boot. A handler exception degrades the rollback
/// fail-closed under <see cref="RollbackDegradeReason.ModHookFailed"/> (the fallback full
/// recycle rebuilds every mod from scratch, so the unknown in-memory state is discarded with
/// the host) and fails the scenario under strict isolation. The listeners are inert outside
/// Atlas runs: the events simply never fire in production.</para></remarks>
internal static class RollbackHooks
{
    /// <summary>Schema version of the hook payloads, carried inside every payload under
    /// <see cref="VersionKey"/> so the shape can evolve without renaming the events.</summary>
    public const int PayloadVersion = 1;

    /// <summary>Event pushed once per capture, immediately after the snapshot is in memory.</summary>
    public const string CapturedEventName = "atlas:rollback:captured";

    /// <summary>Event pushed on every restore, after the SaveGame/global restore and before any
    /// chunk column reload.</summary>
    public const string RestoredEventName = "atlas:rollback:restored";

    /// <summary>Payload key of the schema version (int), present in both payloads.</summary>
    public const string VersionKey = "version";

    /// <summary>Payload key of the capture generation (int), present in both payloads:
    /// increments on every capture, so a mod can correlate a restore with its capture.</summary>
    public const string GenerationKey = "generation";

    /// <summary>Payload key of the restore counter (int), present in the restored payload only:
    /// counts restores of the current generation, starting at 1. With <see cref="GenerationKey"/>
    /// it forms an idempotence token for logging; it is not needed for correctness.</summary>
    public const string RestoreCountKey = "restoreCount";

    /// <summary>Builds the <see cref="CapturedEventName"/> payload.</summary>
    /// <param name="generation">The capture generation.</param>
    /// <returns>The versioned payload.</returns>
    public static TreeAttribute CapturedPayload(int generation)
    {
        var payload = new TreeAttribute();
        payload.SetInt(VersionKey, PayloadVersion);
        payload.SetInt(GenerationKey, generation);
        return payload;
    }

    /// <summary>Builds the <see cref="RestoredEventName"/> payload.</summary>
    /// <param name="generation">The capture generation this restore rolls back to.</param>
    /// <param name="restoreCount">The number of restores of this generation, this one included.</param>
    /// <returns>The versioned payload.</returns>
    public static TreeAttribute RestoredPayload(int generation, int restoreCount)
    {
        TreeAttribute payload = CapturedPayload(generation);
        payload.SetInt(RestoreCountKey, restoreCount);
        return payload;
    }
}
