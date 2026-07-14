using System.Reflection;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

public class AssetsBuildSignalTests
{
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, true)]
    [InlineData(false, 17, true)]
    [InlineData(true, 17, true)]
    public void IsBuilt_Should_SettleOnEitherBranch_When_PacketOrLengthIsSet(
        bool packetAssigned, int serializedLength, bool expected)
        => Assert.Equal(expected, AssetsBuildSignal.IsBuilt(packetAssigned, serializedLength));

    [Fact]
    public void ResolveBoxFields_Should_FindBothFields_When_TheBoxMatchesTheEngineShape()
    {
        (FieldInfo Packet, FieldInfo Length)? fields = AssetsBuildSignal.ResolveBoxFields(typeof(EngineShapedBox));

        Assert.NotNull(fields);
        Assert.Equal("packet", fields.Value.Packet.Name);
        Assert.Equal("Length", fields.Value.Length.Name);
    }

    [Fact]
    public void ResolveBoxFields_Should_FindInheritedLength_When_DeclaredOnABase()
    {
        // The engine declares Length on the BoxedPacket base and packet on the derived
        // BoxedPacket_ServerAssets; a public inherited field must still resolve.
        (FieldInfo Packet, FieldInfo Length)? fields = AssetsBuildSignal.ResolveBoxFields(typeof(DerivedBox));

        Assert.NotNull(fields);
        Assert.Equal(typeof(BoxBase), fields.Value.Length.DeclaringType);
    }

    [Fact]
    public void ResolveBoxFields_Should_ReturnNull_When_ThePacketFieldIsMissing()
        => Assert.Null(AssetsBuildSignal.ResolveBoxFields(typeof(NoPacketBox)));

    [Fact]
    public void ResolveBoxFields_Should_ReturnNull_When_TheLengthFieldIsMissing()
        => Assert.Null(AssetsBuildSignal.ResolveBoxFields(typeof(NoLengthBox)));

    [Fact]
    public void ResolveBoxFields_Should_ReturnNull_When_LengthIsNoLongerAnInt()
        => Assert.Null(AssetsBuildSignal.ResolveBoxFields(typeof(LongLengthBox)));

    [Fact]
    public void ResolveBoxFields_Should_Throw_When_TheBoxTypeIsNull()
        => Assert.Throws<ArgumentNullException>(() => AssetsBuildSignal.ResolveBoxFields(null!));

    [Fact]
    public void DescribeJoinTimeout_Should_NameThePlayerTheBoundAndTheLogs()
    {
        string message = AssetsBuildSignal.DescribeJoinTimeout("AssetsAlice", 1800, "/scratch/data");

        Assert.Contains("'AssetsAlice'", message);
        Assert.Contains("1800", message);
        Assert.Contains("'/scratch/data'", message);
        Assert.Contains("serverAssetsPacket", message);
        Assert.Contains("drifted", message);
    }

#pragma warning disable CS0649 // Fields are resolved and read through reflection only.
#pragma warning disable SA1307, SA1401, S1144, S2933, CA1051, CA1823 // Fields deliberately mirror
    // the engine's box shape: internal lower-case 'packet' on the derived type, public 'Length'.
    private class BoxBase
    {
        public int Length;
    }

    private sealed class DerivedBox : BoxBase
    {
        internal object? packet;
    }

    private sealed class EngineShapedBox
    {
        public int Length;
        internal object? packet;
    }

    private sealed class NoPacketBox
    {
        public int Length;
    }

    private sealed class NoLengthBox
    {
        internal object? packet;
    }

    private sealed class LongLengthBox
    {
        public long Length;
        internal object? packet;
    }
#pragma warning restore CS0649
#pragma warning restore SA1307, SA1401, S1144, S2933, CA1051, CA1823
}
