using System.Text.Json;

namespace Atlas.Cli;

/// <summary>Serializes worker protocol events to a stream, one JSON object per line, flushed
/// after every line so the stream never ends mid-line even if the process dies right after a
/// write. The serialization itself is a pure static, unit-testable without any stream.</summary>
internal sealed class WorkerEventWriter(TextWriter output)
{
    private static readonly JsonSerializerOptions Options = new();

    private readonly object _lock = new();

    /// <summary>Serializes one event to its single-line JSON wire form.</summary>
    /// <param name="evt">The event to serialize.</param>
    /// <returns>The JSON line, without a trailing newline.</returns>
    public static string Serialize(WorkerEvent evt) => JsonSerializer.Serialize(evt, evt.GetType(), Options);

    /// <summary>Writes one event as a line and flushes.</summary>
    /// <param name="evt">The event to write.</param>
    public void Write(WorkerEvent evt)
    {
        string line = Serialize(evt);
        lock (_lock)
        {
            output.WriteLine(line);
            output.Flush();
        }
    }

    /// <summary>Writes a batch of events, one line each, in order.</summary>
    /// <param name="events">The events to write.</param>
    public void WriteAll(IEnumerable<WorkerEvent> events)
    {
        foreach (WorkerEvent evt in events)
        {
            Write(evt);
        }
    }
}
