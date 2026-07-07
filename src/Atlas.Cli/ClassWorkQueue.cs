namespace Atlas.Cli;

/// <summary>The greedy work queue of the orchestrator: scenario classes in discovery order, one
/// class per dispatch, taken by whichever worker loop frees up first. Thread-safe; pure logic
/// (no processes in here), so scheduling order is unit-testable.</summary>
internal sealed class ClassWorkQueue(IEnumerable<string> classes)
{
    private readonly Queue<string> _pending = new(classes);
    private readonly object _sync = new();

    /// <summary>Takes the next class to run, if any is left.</summary>
    /// <param name="className">The taken class; null when the queue is empty.</param>
    /// <returns>True when a class was taken.</returns>
    public bool TryTake(out string? className)
    {
        lock (_sync)
        {
            return _pending.TryDequeue(out className);
        }
    }
}
