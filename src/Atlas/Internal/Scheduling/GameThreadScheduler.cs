using System.Collections.Concurrent;

namespace Atlas.Internal.Scheduling;

/// <summary>Queues work for the game thread; the server pump drains it between ticks.</summary>
internal sealed class GameThreadScheduler : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    /// <summary>Initializes a new instance of the <see cref="GameThreadScheduler"/> class.</summary>
    /// <param name="ownerThreadId">The thread ID that owns this scheduler.</param>
    private GameThreadScheduler(int ownerThreadId) => OwnerThreadId = ownerThreadId;

    /// <summary>Gets the thread ID that owns this scheduler.</summary>
    public int OwnerThreadId { get; }

    /// <summary>Gets a value indicating whether there are pending callbacks in the queue.</summary>
    public bool HasPending => !_queue.IsEmpty;

    /// <summary>Creates and installs a new <see cref="GameThreadScheduler"/> on the current thread.</summary>
    /// <returns>The installed scheduler.</returns>
    public static GameThreadScheduler InstallOnCurrentThread()
    {
        var scheduler = new GameThreadScheduler(Environment.CurrentManagedThreadId);
        SetSynchronizationContext(scheduler);
        return scheduler;
    }

    /// <summary>Posts a callback to the queue for execution on the game thread.</summary>
    /// <param name="d">The callback to post.</param>
    /// <param name="state">The state object to pass to the callback.</param>
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    /// <summary>Runs queued callbacks, including ones posted while draining.</summary>
    /// <remarks>Runs on the game thread.</remarks>
    public void DrainPending()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }

    /// <summary>Runs async work entirely on this scheduler; safe to call from any thread.</summary>
    /// <param name="work">The async work to run.</param>
    /// <returns>A task that completes when the work completes.</returns>
    public Task RunAsync(Func<Task> work)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(
            async _ =>
            {
                SynchronizationContext? previous = Current;
                SetSynchronizationContext(this);
                try
                {
                    await work().ConfigureAwait(true);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    SetSynchronizationContext(previous);
                }
            },
            null);
        return tcs.Task;
    }
}
