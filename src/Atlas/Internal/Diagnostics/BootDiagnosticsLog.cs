using System.Globalization;
using System.Text.RegularExpressions;
using Atlas.Api;
using Vintagestory.API.Common;

namespace Atlas.Internal.Diagnostics;

/// <summary>Pure recorder and filter for engine log entries observed through
/// <c>ILogger.EntryAdded</c>: keeps every entry at <see cref="EnumLogType.Warning"/> or above,
/// formatted, with its source and (best effort) asset path picked out. Source starts out
/// unverified (a <c>"[name] "</c> prefix, if any, is kept as a hint only) and is upgraded to a
/// verified mod id in one of two ways: <see cref="VerifyFromMod"/> confirms it by channel (the
/// entry really did come through that exact mod's own logger), or, only for an entry recorded
/// before any such channel existed, <see cref="ResolveModAttribution"/> matches its hint against
/// the mods the engine actually loaded. The thin shell (<c>ServerHost</c>) owns the actual
/// subscriptions and the mod list; this class only turns one raw entry into a
/// <see cref="BootDiagnosticEntry"/> or discards it, and resolves hints against a mod list it is
/// handed, so the decision is unit-testable without an embedded server (see
/// docs/specs/2026-09-23-boot-diagnostics.md for the researched shapes this is built from).</summary>
/// <remarks><para>Thread-safe: <c>ServerMain.Launch()</c> queues the background server-assets
/// build on a thread-pool thread whose own catch handlers log through the same static
/// <c>ServerMain.Logger</c> (see <c>ServerHost.WaitForAssetsBuildToSettle</c>), so
/// <see cref="Add"/> can fire off the game thread while <see cref="Snapshot"/> is read from it.
/// <see cref="_pendingEntryIndex"/> is <c>[ThreadStatic]</c> rather than shared, so a
/// same-thread <see cref="Add"/> immediately followed by <see cref="VerifyFromMod"/> (see its own
/// remarks on why that ordering is guaranteed) never races a different thread doing the same.</para>
/// <para>Measured cost: under 0.1 ms of handler time per boot (about 1450 <c>EntryAdded</c>
/// calls on a clean boot with no mod under test, one measured machine, AMD Ryzen 9 9900X), not
/// measurable at boot level against this machine's own run-to-run noise (roughly ±100 ms). One
/// delegate call per logged entry at any level (the level filter runs inside <see cref="Add"/>,
/// not before it); only <see cref="EnumLogType.Warning"/> or above is ever formatted, matched or
/// recorded. See docs/specs/2026-09-23-boot-diagnostics.md, "Cost not stated", for the
/// measurement this corrects (an unreplicated, non-interleaved comparison that read as a real
/// 2-3% cost but was run-to-run noise).</para></remarks>
internal sealed partial class BootDiagnosticsLog
{
    /// <summary><see cref="BootDiagnosticEntry.Source"/> when no mod could be verified.</summary>
    internal const string UnknownSource = "unknown";

    // A single log line is at most a few hundred characters and every pattern below is anchored
    // or bounded (no nested quantifiers to backtrack on), so a real match never approaches this;
    // it exists only as a hard ceiling against a pathological input from a mod's log message.
    private const int RegexTimeoutMs = 100;

    // The entry Add() built for the call this exact thread is in the middle of, if any; consumed
    // (and reset to null) by the very next Add() or VerifyFromMod() call on the same thread,
    // never carried across calls. See VerifyFromMod's remarks for why this correlation is safe.
    [ThreadStatic]
    private static int? _pendingEntryIndex;

    private readonly List<BootDiagnosticEntry> _entries = [];
    private readonly object _gate = new();

    // Every mod id and file name the engine actually loaded (ResolveModAttribution), or null
    // before that is known. Null until then rather than empty: an empty set would silently
    // "verify" nothing forever if a caller forgot to call ResolveModAttribution at all, while
    // null keeps every hint unresolved (today's honest default) until the real list arrives.
    private HashSet<string>? _knownModNames;

    // The entry count at the moment BeginModLoggerVerification was first called, or null if it
    // never has been. Only entries recorded BEFORE that count (index-wise) are still eligible for
    // a name-only match in BuildEntry/ResolveModAttribution: everything recorded once a real
    // per-mod-logger channel exists either gets confirmed by VerifyFromMod through that channel,
    // or it did not come through a mod's own logger at all and a matching name is not evidence,
    // only a mod's own hand-written convention wearing a real mod's clothes. Null (never armed)
    // leaves every entry eligible, which is what a bare BootDiagnosticsLog under a pure unit test
    // gets: no channel was ever possible there to prefer over a name match in the first place.
    private int? _channelVerificationArmedAtCount;

    /// <summary>Records one engine log entry, if it is at <see cref="EnumLogType.Warning"/> or
    /// above and is not recognized environmental noise (see <see cref="EnvironmentalNoise"/>).</summary>
    /// <param name="level">The entry's level, exactly as <c>ILogger.EntryAdded</c> reports it.</param>
    /// <param name="rawMessage">The entry's message BEFORE <paramref name="args"/> substitution:
    /// <c>ILogger.EntryAdded</c> fires with the format string, not the already-formatted text
    /// (measured; see the spec). A mod can log a null message; that records as an empty one
    /// rather than throwing from inside someone else's log call.</param>
    /// <param name="args">The format arguments, empty for a parameterless entry. A mod can log
    /// null args; that is treated as no args.</param>
    public void Add(EnumLogType level, string? rawMessage, object?[]? args)
    {
        if (level is not (EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal))
        {
            _pendingEntryIndex = null;
            return;
        }

        rawMessage ??= string.Empty;
        args ??= [];
        string message = Format(rawMessage, args);
        if (MatchOrEmpty(EnvironmentalNoise(), message).Success)
        {
            _pendingEntryIndex = null;
            return;
        }

        (string? hint, string body) = SplitHint(message);
        string? assetPath = FindAssetPath(body);
        lock (_gate)
        {
            _entries.Add(BuildEntry(level, body, assetPath, hint));
            _pendingEntryIndex = _entries.Count - 1;
        }
    }

    /// <summary>Tells the recorder that a real subscription to every currently-loaded mod's own
    /// <c>Mod.Logger.EntryAdded</c> now exists (see <see cref="VerifyFromMod"/> and
    /// <c>ServerHost.SubscribeModLoggers</c>), so entries recorded from this point on always had
    /// an actual channel-verification chance and must never be upgraded to a verified
    /// <see cref="BootDiagnosticEntry.Source"/> by <see cref="ResolveModAttribution"/>'s name match
    /// alone. Only entries recorded before this call (the engine's own per-mod-container load
    /// errors, such as a missing <c>modinfo.json</c> or a failed assembly load, logged while the
    /// mod list itself is still being built, before any mod code, Atlas's own bridge mod included,
    /// has run at all) stay eligible for that name match; there is no channel they could have gone
    /// through instead. Idempotent: only the first call sets the boundary. Never called at all
    /// (a bare <see cref="BootDiagnosticsLog"/> under a pure unit test, with no host wiring up
    /// real per-mod subscriptions) leaves every entry eligible, since no channel was ever possible
    /// there to prefer over a name match in the first place.</summary>
    public void BeginModLoggerVerification()
    {
        lock (_gate)
        {
            _channelVerificationArmedAtCount ??= _entries.Count;
        }
    }

    /// <summary>Marks the entry <see cref="Add"/> just built for THIS thread's current call as
    /// verified for <paramref name="modId"/>: real channel evidence, not a name match. The caller
    /// is a subscription to that exact mod's own <c>Mod.Logger.EntryAdded</c>
    /// (<c>ServerHost.SubscribeModLoggers</c>, wired once <see cref="BeginModLoggerVerification"/>
    /// has run). <c>ModLogger.LogImpl</c> forwards every call into the central logger first
    /// (<c>Parent.Log(logType, "[" + (Mod.Info?.ModID ?? Mod.FileName) + "] " + message, args)</c>,
    /// which is what <see cref="Add"/> observes and records, still unverified), and
    /// <c>LoggerBase.Log</c> only fires the mod's OWN <c>EntryAdded</c> after that forwarding call
    /// returns, synchronously, on the same thread (decompile-measured on 1.21.7 and 1.22.7,
    /// byte-identical on both; see docs/specs/2026-09-23-boot-diagnostics.md). So by the time this
    /// runs, the entry <see cref="Add"/> built a moment ago, on the same thread, for the same
    /// underlying call, is still the last one this thread recorded, and nothing else about it
    /// needs checking: the caller IS that mod's own logger object. A level below
    /// <see cref="EnumLogType.Warning"/> does nothing (<see cref="Add"/> discarded that call
    /// already, so nothing is pending for it); neither does a thread with nothing pending (the
    /// preceding central entry was filtered as environmental noise, or this call simply is not
    /// immediately preceded by an <see cref="Add"/> at all).</summary>
    /// <param name="modId">The verified mod id, or file name fallback, of the logger that
    /// fired.</param>
    /// <param name="level">The entry's level, exactly as that mod's own <c>ILogger.EntryAdded</c>
    /// reports it.</param>
    public void VerifyFromMod(string modId, EnumLogType level)
    {
        if (level is not (EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal))
        {
            return;
        }

        lock (_gate)
        {
            if (_pendingEntryIndex is int index
                && index < _entries.Count
                && _entries[index].Source == UnknownSource)
            {
                _entries[index] = _entries[index] with { Source = modId, SourceHint = null };
            }

            _pendingEntryIndex = null;
        }
    }

    /// <summary>Tells the recorder which mod ids and file names the engine actually loaded, so a
    /// <c>"[name] "</c>-shaped hint recorded before any per-mod-logger channel existed (see
    /// <see cref="BeginModLoggerVerification"/>) can be checked against reality instead of staying
    /// unverified forever: every eligible entry already recorded with a matching hint is upgraded
    /// to a verified <see cref="BootDiagnosticEntry.Source"/> in place. Everything recorded once a
    /// channel existed is left alone here regardless of its hint: if it really came from that
    /// mod's own logger, <see cref="VerifyFromMod"/> already verified it; if a name still matches
    /// without a channel behind it, that is not evidence. Safe to call more than once (a later
    /// call only ever narrows or widens what counts as known; nothing already verified is
    /// un-verified). The one caller, <c>ServerHost.FinishBoot</c>, calls it exactly once, right
    /// after the mod list is known and before the strict check reads the snapshot.</summary>
    /// <param name="knownModNames">Every currently loaded mod's id and file name (both: a mod
    /// whose <c>ModInfo</c> failed to parse is prefixed by its file name instead, see
    /// <see cref="BootDiagnosticEntry.Source"/>).</param>
    public void ResolveModAttribution(IEnumerable<string> knownModNames)
    {
        ArgumentNullException.ThrowIfNull(knownModNames);
        lock (_gate)
        {
            _knownModNames = new HashSet<string>(knownModNames, StringComparer.Ordinal);
            int eligibleCount = _channelVerificationArmedAtCount ?? _entries.Count;
            for (int i = 0; i < eligibleCount; i++)
            {
                BootDiagnosticEntry entry = _entries[i];
                if (entry.Source == UnknownSource
                    && entry.SourceHint is { } hint
                    && _knownModNames.Contains(hint))
                {
                    _entries[i] = entry with { Source = hint, SourceHint = null };
                }
            }
        }
    }

    /// <summary>Snapshots every entry recorded so far.</summary>
    /// <returns>The recorded entries, oldest first.</returns>
    public IReadOnlyList<BootDiagnosticEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    // Callers hold _gate: _knownModNames is read and _entries is written under the same lock
    // ResolveModAttribution uses to swap _knownModNames and upgrade already-recorded entries, so
    // an Add racing a ResolveModAttribution call sees one or the other, never a half-applied set.
    // The inline name match only ever applies before BeginModLoggerVerification has run (see its
    // remarks): once armed, a brand new entry is index >= the armed count by construction (indices
    // only grow), so it is never eligible here either, matching ResolveModAttribution's own gate.
    private BootDiagnosticEntry BuildEntry(EnumLogType level, string message, string? assetPath, string? hint)
    {
        bool eligibleForNameMatch = _channelVerificationArmedAtCount == null;
        string source = eligibleForNameMatch && hint != null && _knownModNames?.Contains(hint) == true
            ? hint
            : UnknownSource;
        return new BootDiagnosticEntry(level, source, message, assetPath, source == UnknownSource ? hint : null);
    }

    private static string Format(string rawMessage, object?[] args)
    {
        if (args.Length == 0)
        {
            return rawMessage;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, rawMessage, args);
        }
        catch (FormatException)
        {
            // A message whose placeholders do not match its args is still worth keeping,
            // unformatted, over losing the entry entirely.
            return rawMessage;
        }
    }

    private static (string? Hint, string Message) SplitHint(string message)
    {
        Match match = MatchOrEmpty(BracketPrefix(), message);
        return match.Success ? (match.Groups["name"].Value, message[match.Length..]) : (null, message);
    }

    private static string? FindAssetPath(string message)
    {
        Match match = MatchOrEmpty(AssetPathToken(), message);
        return match.Success ? match.Value.TrimEnd('.', ',', '\'', '"') : null;
    }

    // What a RegexMatchTimeoutException means at each of the three call sites above: always
    // "could not decide, so decide as if it had not matched", never dropping the entry or
    // throwing back into the engine's own log call. Concretely: EnvironmentalNoise falls through
    // to recording the entry instead of filtering it out, BracketPrefix records it with no hint
    // (Source "unknown") instead of the parsed name, and AssetPathToken records it with
    // AssetPath null instead of the extracted token.
    private static Match MatchOrEmpty(Regex regex, string input)
    {
        try
        {
            return regex.Match(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return Match.Empty;
        }
    }

    // Measured: LoggerBase.ModLogger prefixes every message with "[modid] " (or "[filename] "
    // when ModInfo failed to parse) before it reaches ServerMain.Logger.EntryAdded; everything
    // else observed on that event is either the engine's own central logger, unprefixed, or a
    // mod writing its own bracketed convention by hand through the same central logger (the two
    // are indistinguishable here). Deliberately wider than a mod-id charset (unlike the old
    // single-purpose parse this replaces): a file-name fallback can contain a dot ("MyMod.dll"),
    // and this text is never trusted on its own any more, only checked against the real mod list
    // in ResolveModAttribution, so there is nothing to gain from pre-validating its shape. Not
    // run through EngineCompat: ILogger is public mod API, not an internal member whose shape is
    // known to move across supported engine versions (see the spec's "engine surface" section
    // for why).
    [GeneratedRegex(@"^\[(?<name>[^\]]+)\] ", RegexOptions.None, matchTimeoutMilliseconds: RegexTimeoutMs)]
    private static partial Regex BracketPrefix();

    // ponytail: first-match heuristic over the free-form message text, not a structured field the
    // engine provides. Picks out the first "domain:token" substring, which is right for a message
    // naming one asset and only approximate for one naming several (e.g. a recipe error naming
    // both its output and its missing ingredient picks the first one mentioned). Upgrade path: if
    // a future engine version exposes the failing AssetLocation as structured data, read that
    // instead of parsing the message. The leading (?<![\w.]) excludes a stack-trace fragment like
    // "File.cs:line 42" (would otherwise match "cs:line"): a real domain never follows a word
    // character or a dot.
    [GeneratedRegex(
        @"(?<![\w.])[a-z][a-z0-9_]*:[A-Za-z0-9_\-./]+", RegexOptions.None, matchTimeoutMilliseconds: RegexTimeoutMs)]
    private static partial Regex AssetPathToken();

    // Measured (docs/specs/2026-09-23-boot-diagnostics.md "Environmental noise"): the engine logs
    // this exact shape whenever a tick runs long, which a loaded CI runner triggers on a clean
    // boot with no mod under test at all (138 scratch logs surveyed across two CI runs, 9 hits,
    // zero relation to any asset). It is about the machine, not a mod's assets, so it is never a
    // boot diagnostic and must never fail StrictBootDiagnostics on a slow box. The Stratum fork
    // (which Atlas also targets; see StratumParity) logs the same warning from its own ServerMain,
    // worded "Server may be overloaded. ..." instead, so the pattern accepts both wordings. Filtered
    // here, before the entry is ever recorded, rather than kept with a flag: that keeps
    // BootDiagnosticEntry and IWorldSession.BootDiagnostics exactly as simple as before this
    // fix, with nothing downstream needing to know this shape exists.
    [GeneratedRegex(
        @"^Server (may be )?overloaded\. A tick took \d+ms to complete\.$",
        RegexOptions.None,
        matchTimeoutMilliseconds: RegexTimeoutMs)]
    private static partial Regex EnvironmentalNoise();
}
