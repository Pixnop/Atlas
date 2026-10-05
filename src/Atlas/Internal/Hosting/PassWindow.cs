namespace Atlas.Internal.Hosting;

/// <summary>The samples one <see cref="PassTimingCollector"/> window collected, one entry per pass,
/// oldest first: the engine's busy time in whole milliseconds and Atlas's own figure for the same
/// pass in microseconds.</summary>
internal sealed class PassWindow
{
    /// <summary>Gets the engine's busy-time samples, in milliseconds.</summary>
    public List<long> BusyMs { get; } = [];

    /// <summary>Gets Atlas's own busy-time figures, in microseconds, one per entry of
    /// <see cref="BusyMs"/> (see <see cref="PassTimingStatistics.BusyMicroseconds"/>).</summary>
    public List<double> BusyMicroseconds { get; } = [];
}
