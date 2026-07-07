namespace Atlas.Cli;

/// <summary>Computes how many worker subprocesses `--parallel` runs. Pure.</summary>
internal static class ParallelDegree
{
    /// <summary>Resolves the worker count: the explicit request when one was given, otherwise
    /// half the processor count (the stage 0 calibration: each embedded server wants roughly two
    /// cores before workers start slowing each other down), always at least 1 and never more
    /// than the number of classes (a worker without a class to run is pure overhead).</summary>
    /// <param name="requested">The explicit `--parallel N` value, or null for the default.</param>
    /// <param name="processorCount">The machine's logical processor count.</param>
    /// <param name="classCount">How many scenario classes there are to dispatch.</param>
    /// <returns>The number of workers to spawn.</returns>
    public static int Resolve(int? requested, int processorCount, int classCount)
    {
        int degree = requested ?? Math.Max(1, processorCount / 2);
        return Math.Min(degree, Math.Max(1, classCount));
    }
}
