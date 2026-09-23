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
/// <see cref="_pending"/> is <c>[ThreadStatic]</c> rather than shared, so a same-thread
/// <see cref="Add"/> immediately followed by <see cref="VerifyFromMod"/> never races a different
/// thread doing the same; see <see cref="VerifyFromMod"/>'s own remarks for why the pending entry
/// still has to be checked, not just trusted, even on the right thread.</para>
/// <para>Cost: figures from a review pass on 2026-09-23, not reproducible from a committed
/// command, measured on the central-logger handler before the per-mod-logger subscriptions were
/// added. Those add one delegate call per <c>Mod.Logger</c> call and one lock per
/// Warning-or-above entry, and were not measured separately.</para></remarks>
internal sealed partial class BootDiagnosticsLog
{
    /// <summary><see cref="BootDiagnosticEntry.Source"/> when no mod could be verified.</summary>
    internal const string UnknownSource = "unknown";

    // A single log line is at most a few hundred characters and every pattern below is anchored
    // or bounded (no nested quantifiers to backtrack on), so a real match never approaches this;
    // it exists only as a hard ceiling against a pathological input from a mod's log message.
    private const int RegexTimeoutMs = 100;

    // The entry Add() built for the call this exact thread is in the middle of, if any; consumed
    // (and reset to null) by the very next Add() or VerifyFromMod() call on the same thread, never
    // carried across calls. Keeps the owner and the original, unnormalised message/args (not just
    // the index) because being "the last thing this thread recorded" does not by itself mean it is
    // THIS call's entry: see VerifyFromMod's remarks for the two real ways that assumption breaks.
    [ThreadStatic]
    private static (BootDiagnosticsLog Owner, int Index, string? RawMessage, object?[]? Args)? _pending;

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
            _pending = null;
            return;
        }

        // Kept as received (before the null normalisation below) so VerifyFromMod can compare
        // against exactly what a mod's own EntryAdded reports: that event never normalises either.
        string? originalRawMessage = rawMessage;
        object?[]? originalArgs = args;

        rawMessage ??= string.Empty;
        args ??= [];
        string message = Format(rawMessage, args);
        if (MatchOrEmpty(EnvironmentalNoise(), message).Success)
        {
            _pending = null;
            return;
        }

        (string? hint, string body) = SplitHint(message);
        string? assetPath = FindAssetPath(body);
        lock (_gate)
        {
            _entries.Add(BuildEntry(level, body, assetPath, hint));
            _pending = (this, _entries.Count - 1, originalRawMessage, originalArgs);
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

    /// <summary>Marks the entry <see cref="Add"/> built for THIS thread's current call as verified
    /// for <paramref name="modId"/>, but only once it is checked to actually BE that call's entry:
    /// real channel evidence, not a guess about which pending entry is close enough. The caller is
    /// a subscription to that exact mod's own <c>Mod.Logger.EntryAdded</c>
    /// (<c>ServerHost.SubscribeModLoggers</c>, wired once <see cref="BeginModLoggerVerification"/>
    /// has run). <c>ModLogger.LogImpl</c> forwards every call into the central logger first
    /// (<c>Parent.Log(logType, "[" + (Mod.Info?.ModID ?? Mod.FileName) + "] " + message, args)</c>,
    /// which is what <see cref="Add"/> observes and records, still unverified, keeping the exact
    /// <paramref name="args"/> array reference), and <c>LoggerBase.Log</c> fires the mod's own
    /// <c>EntryAdded</c> with that same message and args right after, synchronously, on the same
    /// thread (decompile-measured on 1.21.7, 1.22.3, 1.22.7 and Stratum).
    /// <para>Two real engine paths still break "the last entry this thread recorded is this
    /// call's entry":</para>
    /// <para>(a) A log written from inside another central-logger <c>EntryAdded</c> handler runs,
    /// on the same thread, between <see cref="Add"/> and the mod's own <c>EntryAdded</c>, and
    /// overwrites the pending slot before this call ever sees it. Vanilla has one such subscriber
    /// on every version: <c>ServerSystemMonitor.OnEntryAdded</c> calls <c>server.Stop(...)</c>,
    /// which itself logs a final message at <see cref="EnumLogType.Error"/> once errors exceed
    /// <c>DieAboveErrorCount</c>. Any mod subscribed to <c>api.Logger.EntryAdded</c> that logs from
    /// its own handler does the same.</para>
    /// <para>(b) <c>ServerMain.Stop</c> ends by clearing the central logger's own watchers
    /// (<c>Logger.ClearWatchers()</c>), which removes <see cref="Add"/>'s subscription but not a
    /// mod's own per-mod one. A mod that logs through <c>Mod.Logger</c> during <c>Dispose</c> then
    /// fires this with nothing pending at all, or a leftover, unrelated entry.</para>
    /// <para>Comparing the owning instance, the exact <paramref name="args"/> array reference
    /// (never copied along this path) and the reconstructed <c>"[" + modId + "] " + message</c>
    /// text against what <see cref="Add"/> actually recorded catches both: when any of the three
    /// disagree, this call marks nothing, and the pending entry stays <c>"unknown"</c> rather than
    /// being credited to the wrong mod - the entry is lost, not misattributed. A level below
    /// <see cref="EnumLogType.Warning"/> does nothing (<see cref="Add"/> discarded that call
    /// already, so nothing correlates to it).</para></summary>
    /// <param name="modId">The verified mod id, or file name fallback, of the logger that
    /// fired.</param>
    /// <param name="level">The entry's level, exactly as that mod's own <c>ILogger.EntryAdded</c>
    /// reports it.</param>
    /// <param name="message">This entry's own, unprefixed message (before <paramref name="args"/>
    /// substitution), exactly as that mod's own <c>ILogger.EntryAdded</c> reports it.</param>
    /// <param name="args">This entry's own format arguments, exactly as that mod's own
    /// <c>ILogger.EntryAdded</c> reports them: the very array <see cref="Add"/> was called with,
    /// unmodified, which is what makes the reference-equality check below meaningful.</param>
    public void VerifyFromMod(string modId, EnumLogType level, string? message, object?[]? args)
    {
        if (level is not (EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal))
        {
            return;
        }

        lock (_gate)
        {
            if (_pending is { } p
                && ReferenceEquals(p.Owner, this)
                && ReferenceEquals(p.Args, args)
                && string.Equals(p.RawMessage, "[" + modId + "] " + message, StringComparison.Ordinal)
                && _entries[p.Index].Source == UnknownSource)
            {
                _entries[p.Index] = _entries[p.Index] with { Source = modId, SourceHint = null };
            }

            _pending = null;
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
