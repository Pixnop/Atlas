using System.Collections.Concurrent;
using System.Diagnostics;

namespace Atlas.ClientSpike;

/// <summary>Thread-safe named marks on one stopwatch.</summary>
internal sealed class Timeline
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentDictionary<string, TimeSpan> _marks = new();
    private readonly ConcurrentQueue<string> _log = new();

    public TimeSpan Now => _clock.Elapsed;

    public void Mark(string name, string? detail = null)
    {
        TimeSpan at = _clock.Elapsed;
        if (!_marks.TryAdd(name, at))
        {
            return;
        }

        _log.Enqueue($"{at.TotalSeconds,8:F2}s  {name}{(detail == null ? string.Empty : "  " + detail)}");
    }

    public bool Has(string name) => _marks.ContainsKey(name);

    public TimeSpan? At(string name) => _marks.TryGetValue(name, out TimeSpan t) ? t : null;

    public string Dump() => string.Join(Environment.NewLine, _log);
}
