using System.Diagnostics;

namespace Atlas.Internal.Hosting;

/// <summary>The pollable core of the pre-dispose assets-build wait (see
/// <c>ServerHost.WaitForAssetsBuildToSettle</c>): loops a completion signal until it settles or a
/// bounded timeout elapses. Kept free of engine types and reflection so the loop and its timeout
/// decision are testable without booting a server; the reflective probe stays in the thin shell.</summary>
internal static class AssetsBuildSettle
{
    /// <summary>Polls <paramref name="built"/> until it reports completion or the timeout elapses.</summary>
    /// <param name="built">The completion signal; sampled once before any sleep.</param>
    /// <param name="timeout">Upper bound on the total wait.</param>
    /// <param name="pollInterval">Sleep between samples.</param>
    /// <returns><see langword="true"/> when the signal settled in time; <see langword="false"/> on
    /// timeout, in which case the caller decides how loudly to proceed.</returns>
    public static bool Wait(Func<bool> built, TimeSpan timeout, TimeSpan pollInterval)
    {
        ArgumentNullException.ThrowIfNull(built);
        var elapsed = Stopwatch.StartNew();
        while (!built())
        {
            if (elapsed.Elapsed >= timeout)
            {
                return false;
            }

            Thread.Sleep(pollInterval);
        }

        return true;
    }
}
