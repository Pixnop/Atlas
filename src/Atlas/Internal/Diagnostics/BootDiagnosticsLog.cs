using System.Globalization;
using System.Text.RegularExpressions;
using Atlas.Api;
using Vintagestory.API.Common;

namespace Atlas.Internal.Diagnostics;

/// <summary>Pure recorder and filter for engine log entries observed through
/// <c>ILogger.EntryAdded</c>: keeps every entry at <see cref="EnumLogType.Warning"/> or above,
/// formatted, with its source and (best effort) asset path picked out. The thin shell
/// (<c>ServerHost</c>) owns the actual subscription; this class only turns one raw entry into a
/// <see cref="BootDiagnosticEntry"/> or discards it, so the decision is unit-testable without an
/// embedded server (see docs/specs/2026-09-23-boot-diagnostics.md for the researched shapes this
/// is built from).</summary>
/// <remarks>Thread-safe: <c>ServerMain.Launch()</c> queues the background server-assets build on
/// a thread-pool thread whose own catch handlers log through the same static
/// <c>ServerMain.Logger</c> (see <c>ServerHost.WaitForAssetsBuildToSettle</c>), so
/// <see cref="Add"/> can fire off the game thread while <see cref="Snapshot"/> is read from it.</remarks>
internal sealed partial class BootDiagnosticsLog
{
    // A single log line is at most a few hundred characters and every pattern below is anchored
    // or bounded (no nested quantifiers to backtrack on), so a real match never approaches this;
    // it exists only as a hard ceiling against a pathological input from a mod's log message.
    private const int RegexTimeoutMs = 100;

    private readonly List<BootDiagnosticEntry> _entries = [];
    private readonly object _gate = new();

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
            return;
        }

        rawMessage ??= string.Empty;
        args ??= [];
        string message = Format(rawMessage, args);
        if (MatchOrEmpty(EnvironmentalNoise(), message).Success)
        {
            return;
        }

        (string source, string body) = SplitSource(message);
        var entry = new BootDiagnosticEntry(level, source, body, FindAssetPath(body));
        lock (_gate)
        {
            _entries.Add(entry);
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

    private static (string Source, string Message) SplitSource(string message)
    {
        Match match = MatchOrEmpty(ModPrefix(), message);
        return match.Success
            ? (match.Groups["modid"].Value, message[match.Length..])
            : ("engine", message);
    }

    private static string? FindAssetPath(string message)
    {
        Match match = MatchOrEmpty(AssetPathToken(), message);
        return match.Success ? match.Value.TrimEnd('.', ',', '\'', '"') : null;
    }

    // What a RegexMatchTimeoutException means at each of the three call sites above: always
    // "could not decide, so decide as if it had not matched", never dropping the entry or
    // throwing back into the engine's own log call. Concretely: EnvironmentalNoise falls through
    // to recording the entry instead of filtering it out, ModPrefix records it with source
    // "engine" instead of the parsed mod id, and AssetPathToken records it with AssetPath null
    // instead of the extracted token.
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

    // Measured: LoggerBase.ModLogger prefixes every message with "[modid] " before it reaches
    // ServerMain.Logger.EntryAdded; everything else observed on that event is the engine's own
    // central logger, unprefixed. Not run through EngineCompat: ILogger is public mod API, not an
    // internal member whose shape is known to move across supported engine versions (see the
    // spec's "engine surface" section for why).
    [GeneratedRegex(@"^\[(?<modid>[A-Za-z0-9_\-]+)\] ", RegexOptions.None, matchTimeoutMilliseconds: RegexTimeoutMs)]
    private static partial Regex ModPrefix();

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
