namespace Atlas.Engine.Tests.Support;

/// <summary>Captures what Atlas writes to <see cref="Console.Error"/> while an operation runs.
/// Several diagnostics are observable nowhere else (the per-class isolation summary, the
/// rollback degrade warning, the restore-cost line, the teardown warnings), and
/// <see cref="Console.Error"/> is process-wide state shared with every other test in this
/// single-threaded suite, so it is always restored, including when the operation throws.</summary>
internal static class Stderr
{
    /// <summary>Runs <paramref name="operation"/> with stderr redirected.</summary>
    /// <param name="operation">The operation to run.</param>
    /// <returns>Everything the operation wrote to stderr.</returns>
    public static async Task<string> CaptureAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        (_, string text) = await CaptureAsync(async () =>
        {
            await operation();
            return true;
        });
        return text;
    }

    /// <summary>Runs <paramref name="operation"/> with stderr redirected, keeping its result.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The operation to run.</param>
    /// <returns>The operation's result and everything it wrote to stderr.</returns>
    public static async Task<(T Result, string Text)> CaptureAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var captured = new StringWriter();
        TextWriter real = Console.Error;
        try
        {
            Console.SetError(captured);
            return (await operation(), captured.ToString());
        }
        finally
        {
            Console.SetError(real);
        }
    }
}
