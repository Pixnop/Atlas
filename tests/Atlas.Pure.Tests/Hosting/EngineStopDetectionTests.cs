using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

public class EngineStopDetectionTests
{
    [Fact]
    public void IsEngineInitiatedStop_Should_ReturnTrue_When_EngineStoppedWithoutAtlasRequest()
        => Assert.True(EngineStopDetection.IsEngineInitiatedStop(engineStopped: true, atlasStopRequested: false));

    [Fact]
    public void IsEngineInitiatedStop_Should_ReturnFalse_When_AtlasRequestedTheStop()
        => Assert.False(EngineStopDetection.IsEngineInitiatedStop(engineStopped: true, atlasStopRequested: true));

    [Fact]
    public void IsEngineInitiatedStop_Should_ReturnFalse_When_TheEngineIsStillRunning()
    {
        Assert.False(EngineStopDetection.IsEngineInitiatedStop(engineStopped: false, atlasStopRequested: false));
        Assert.False(EngineStopDetection.IsEngineInitiatedStop(engineStopped: false, atlasStopRequested: true));
    }

    [Fact]
    public void Describe_Should_PointAtTheServerLogs_When_Worded()
    {
        string message = EngineStopDetection.Describe("/scratch/atlas/deadbeef");

        // The engine does not retain its stop reason anywhere readable, so the actionable part
        // of the message is the pointer to the engine's own log, where both the reason and the
        // failing thread's stack were written.
        Assert.Contains("stopped itself", message);
        Assert.Contains("Exception during Process", message);
        Assert.Contains("/scratch/atlas/deadbeef/Logs", message);
        Assert.Contains("server-main.log", message);
    }

    [Fact]
    public void EngineStoppedException_Should_CarryTheMessageVerbatim_When_Thrown()
    {
        var exception = new EngineStoppedException(EngineStopDetection.Describe("/data"));

        Assert.Contains("/data/Logs", exception.Message);
    }
}
