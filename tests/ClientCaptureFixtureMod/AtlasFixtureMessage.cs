using ProtoBuf;

namespace ClientCaptureFixtureMod;

/// <summary>The fixture's one channel message, the exact registration shape a shipping mod
/// uses (Caminus's <c>OverlayPacket</c>): a protobuf-net contract with one member.</summary>
[ProtoContract]
public sealed class AtlasFixtureMessage
{
    [ProtoMember(1)]
    public string Text { get; set; } = string.Empty;
}
