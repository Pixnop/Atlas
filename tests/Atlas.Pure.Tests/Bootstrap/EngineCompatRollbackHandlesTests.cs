namespace Atlas.Pure.Tests.Bootstrap;

/// <summary>Covers the one corner of <see cref="EngineCompat"/> that <see cref="EngineCompatTests"/>
/// cannot reach with a fake shape and that no E2E boot reaches either: <see cref="EngineCompat.ChunkThreadField"/>,
/// <see cref="EngineCompat.GameDatabaseField"/> and <see cref="EngineCompat.TryUnloadChunk"/> are
/// deliberately left out of <see cref="EngineCompat.ValidateAtBoot"/> (rollback degrades to a host
/// recycle rather than failing a boot), so nothing forces their <c>Lazy&lt;T&gt;</c> initializers
/// to run unless a test reads the properties directly against the referenced engine.</summary>
public class EngineCompatRollbackHandlesTests
{
    [Fact]
    public void RollbackHandles_Should_ResolveAgainstTheReferencedEngine()
    {
        EngineCompat.ValidateAtBoot();

        Assert.Equal("chunkThread", EngineCompat.ChunkThreadField.Name);
        Assert.Equal("gameDatabase", EngineCompat.GameDatabaseField.Name);
        Assert.Equal("TryUnloadChunk", EngineCompat.TryUnloadChunk.Name);
    }
}
