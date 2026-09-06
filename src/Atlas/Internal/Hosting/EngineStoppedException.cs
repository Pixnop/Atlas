using System.Diagnostics.CodeAnalysis;

namespace Atlas.Internal.Hosting;

/// <summary>Thrown by the game-thread pump when the embedded engine stopped itself (see
/// <see cref="EngineStopDetection"/>): the pump exits, the stop is recorded as the host's
/// crash, and callers observe it wrapped in the same <c>ServerCrashedException</c> as any other
/// game-thread death, so a scenario fails fast instead of hanging on a pump that keeps calling
/// <c>Process()</c> on a stopped server.</summary>
[SuppressMessage(
    "Critical Code Smell",
    "S3871:Exception types should be public",
    Justification = "No consumer can catch it: it is thrown on the game thread and only ever reaches a consumer as the InnerException of the public ServerCrashedException, which is the type scenarios handle.")]
internal sealed class EngineStoppedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EngineStoppedException"/> class.</summary>
    /// <param name="message">The crash message (see <see cref="EngineStopDetection.Describe"/>).</param>
    public EngineStoppedException(string message)
        : base(message)
    {
    }
}
