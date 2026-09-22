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
internal sealed class BootDiagnosticsLog
{
    // Measured: LoggerBase.ModLogger prefixes every message with "[modid] " before it reaches
    // ServerMain.Logger.EntryAdded; everything else observed on that event is the engine's own
    // central logger, unprefixed. Not run through EngineCompat: ILogger is public mod API, not an
    // internal member whose shape is known to move across supported engine versions (see the
    // spec's "engine surface" section for why).
    private static readonly Regex ModPrefix = new(@"^\[(?<modid>[A-Za-z0-9_\-]+)\] ", RegexOptions.Compiled);

    // ponytail: first-match heuristic over the free-form message text, not a structured field the
    // engine provides. Picks out the first "domain:token" substring, which is right for a message
    // naming one asset and only approximate for one naming several (e.g. a recipe error naming
    // both its output and its missing ingredient picks the first one mentioned). Upgrade path: if
    // a future engine version exposes the failing AssetLocation as structured data, read that
    // instead of parsing the message.
    private static readonly Regex AssetPathToken = new(@"[a-z][a-z0-9_]*:[A-Za-z0-9_\-./]+", RegexOptions.Compiled);

    private readonly List<BootDiagnosticEntry> _entries = [];
    private readonly object _gate = new();

    /// <summary>Records one engine log entry, if it is at <see cref="EnumLogType.Warning"/> or
    /// above.</summary>
    /// <param name="level">The entry's level, exactly as <c>ILogger.EntryAdded</c> reports it.</param>
    /// <param name="rawMessage">The entry's message BEFORE <paramref name="args"/> substitution:
    /// <c>ILogger.EntryAdded</c> fires with the format string, not the already-formatted text
    /// (measured; see the spec).</param>
    /// <param name="args">The format arguments, empty for a parameterless entry.</param>
    public void Add(EnumLogType level, string rawMessage, object[] args)
    {
        if (level is not (EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal))
        {
            return;
        }

        string message = Format(rawMessage, args);
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

    private static string Format(string rawMessage, object[] args)
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
        Match match = ModPrefix.Match(message);
        return match.Success
            ? (match.Groups["modid"].Value, message[match.Length..])
            : ("engine", message);
    }

    private static string? FindAssetPath(string message)
    {
        Match match = AssetPathToken.Match(message);
        return match.Success ? match.Value.TrimEnd('.', ',', '\'', '"') : null;
    }
}
