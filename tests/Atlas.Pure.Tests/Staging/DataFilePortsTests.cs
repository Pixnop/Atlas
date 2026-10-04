using System.Text;
using Atlas.Internal.Staging;

namespace Atlas.Pure.Tests.Staging;

/// <summary>Contract of the <c>{{atlas:port:NAME}}</c> token: what it matches, that a name keeps
/// one port for the host, that two names never share one, and that anything else that starts
/// with the reserved prefix fails the boot instead of reaching the mod unresolved.</summary>
public class DataFilePortsTests
{
    private readonly List<int[]> _takenAtEachDraw = [];
    private int _next = 40000;

    [Fact]
    public void Resolve_Should_ReplaceTheTokenWithTheDrawnPort_When_TheTextHoldsOne()
    {
        string resolved = Ports().Resolve("""{ "port": {{atlas:port:metrics}}, "host": "127.0.0.1" }""", "mod.json");

        Assert.Equal("""{ "port": 40000, "host": "127.0.0.1" }""", resolved);
    }

    [Fact]
    public void Resolve_Should_ReplaceEveryOccurrenceWithOnePort_When_TheNameRepeatsInOneFile()
    {
        string resolved = Ports().Resolve("a={{atlas:port:x}};b={{atlas:port:x}}", "mod.cfg");

        Assert.Equal("a=40000;b=40000", resolved);
        Assert.Single(_takenAtEachDraw);
    }

    [Fact]
    public void Resolve_Should_GiveTheSamePort_When_TheNameRepeatsAcrossFiles()
    {
        DataFilePorts ports = Ports();

        string first = ports.Resolve("{{atlas:port:x}}", "a.json");
        string second = ports.Resolve("port: {{atlas:port:x}}", "b.yaml");

        Assert.Equal("40000", first);
        Assert.Equal("port: 40000", second);
        Assert.Equal(40000, ports.PortOf("x"));
    }

    [Fact]
    public void Resolve_Should_GiveDistinctPortsAndHandTheDrawTheOnesTaken_When_NamesDiffer()
    {
        DataFilePorts ports = Ports();

        string resolved = ports.Resolve("{{atlas:port:a}} {{atlas:port:b}} {{atlas:port:c}}", "mod.json");

        Assert.Equal("40000 40001 40002", resolved);
        Assert.Equal([[], [40000], [40000, 40001]], _takenAtEachDraw);
    }

    [Fact]
    public void Resolve_Should_TreatNamesAsCaseSensitive_When_OnlyTheCaseDiffers()
    {
        DataFilePorts ports = Ports();

        string resolved = ports.Resolve("{{atlas:port:Web}} {{atlas:port:web}}", "mod.json");

        Assert.Equal("40000 40001", resolved);
    }

    [Theory]
    [InlineData("{{atlas:port:a}}")]
    [InlineData("{{atlas:port:A_b-c.9}}")]
    [InlineData("{{atlas:port:9}}")]
    public void Resolve_Should_Accept_When_TheNameUsesLettersDigitsUnderscoreDotOrDash(string text)
        => Assert.Equal("40000", Ports().Resolve(text, "mod.json"));

    [Theory]
    [InlineData("{{other:port:x}}")]
    [InlineData("{{ atlas:port:x }}")]
    [InlineData("{ \"a\": {\"b\": 1} }")]
    [InlineData("atlas:port:x")]
    [InlineData("{{x}}")]
    public void Resolve_Should_LeaveTheTextAlone_When_ItHoldsNoAtlasToken(string text)
    {
        DataFilePorts ports = Ports();

        Assert.Equal(text, ports.Resolve(text, "mod.json"));
        Assert.Empty(_takenAtEachDraw);
    }

    [Theory]
    [InlineData("{{atlas:port:}}")]
    [InlineData("{{atlas:port:two words}}")]
    [InlineData("{{atlas:port:x}")]
    [InlineData("{{atlas:port:x")]
    [InlineData("{{atlas:Port:x}}")]
    [InlineData("{{atlas:host:x}}")]
    [InlineData("{{atlas:}}")]
    public void Resolve_Should_FailNamingTheFileAndTheForm_When_ATokenOfTheReservedPrefixIsNotRecognized(string text)
    {
        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => Ports().Resolve("before " + text + " after", "ModConfig/mod.json"));

        Assert.Contains("ModConfig/mod.json", ex.Message);
        Assert.Contains("{{atlas:port:NAME}}", ex.Message);
    }

    [Fact]
    public void PortOf_Should_ThrowNamingTheKnownNames_When_TheNameIsUnknown()
    {
        DataFilePorts ports = Ports();
        ports.Resolve("{{atlas:port:web}} {{atlas:port:admin}}", "mod.json");

        ArgumentException ex = Assert.Throws<ArgumentException>(() => ports.PortOf("metrics"));

        Assert.Equal("name", ex.ParamName);
        Assert.Contains("'metrics'", ex.Message);
        Assert.Contains("admin, web", ex.Message);
    }

    [Fact]
    public void PortOf_Should_SayNoFileDeclaredOne_When_NoTokenWasResolved()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => Ports().PortOf("web"));

        Assert.Contains("'web'", ex.Message);
        Assert.Contains("[AtlasDataFiles]", ex.Message);
    }

    [Fact]
    public void PortOf_Should_RejectANullName_When_Asked()
        => Assert.Throws<ArgumentNullException>(() => Ports().PortOf(null!));

    [Fact]
    public void MayHoldToken_Should_BeTrueOnlyForBytesWithThePrefix()
    {
        Assert.True(DataFilePorts.MayHoldToken(Encoding.UTF8.GetBytes("x {{atlas:port:a}} y")));
        Assert.True(DataFilePorts.MayHoldToken(Encoding.UTF8.GetBytes("{{atlas:")));
        Assert.False(DataFilePorts.MayHoldToken(Encoding.UTF8.GetBytes("{{atlas")));
        Assert.False(DataFilePorts.MayHoldToken(new byte[] { 0, 1, 2, 255, 254 }));
        Assert.False(DataFilePorts.MayHoldToken([]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void MayHoldToken_Should_FindThePrefixInAStream_When_EachReadReturnsFewBytes(int chunk)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("padding padding {{atlas:port:a}} tail");
        using var stream = new TrickleStream(bytes, chunk);

        Assert.True(DataFilePorts.MayHoldToken(stream));
    }

    [Fact]
    public void MayHoldToken_Should_FindThePrefix_When_ItStraddlesTheEdgeOfTheScanBuffer()
    {
        byte[] bytes = new byte[DataFilePorts.ScanBufferSize * 3];
        for (int start = DataFilePorts.ScanBufferSize - 16; start <= DataFilePorts.ScanBufferSize + 16; start++)
        {
            Array.Fill(bytes, (byte)'x');
            "{{atlas:"u8.CopyTo(bytes.AsSpan(start));
            using var stream = new MemoryStream(bytes);

            Assert.True(DataFilePorts.MayHoldToken(stream), $"prefix at offset {start}");
        }
    }

    [Fact]
    public void MayHoldToken_Should_BeFalseForAStream_When_NoPrefixIsInItWhateverTheReadSize()
    {
        byte[] noPrefix = [.. Encoding.UTF8.GetBytes("{{atlas"), .. new byte[DataFilePorts.ScanBufferSize * 2], .. "{atlas:"u8.ToArray()];

        using (var whole = new MemoryStream(noPrefix))
        {
            Assert.False(DataFilePorts.MayHoldToken(whole));
        }

        using var trickle = new TrickleStream(noPrefix, 5);
        Assert.False(DataFilePorts.MayHoldToken(trickle));
        Assert.False(DataFilePorts.MayHoldToken(new MemoryStream()));
    }

    [Fact]
    public void Constructor_Should_DrawFromTheSharedFreePortHelper_When_NoDrawIsGiven()
    {
        var ports = new DataFilePorts();

        int port = int.Parse(ports.Resolve("{{atlas:port:real}}", "mod.json"), System.Globalization.CultureInfo.InvariantCulture);

        Assert.InRange(port, 1024, 65535);
        Assert.Equal(port, ports.PortOf("real"));
    }

    private DataFilePorts Ports() => new(taken =>
    {
        _takenAtEachDraw.Add([.. taken]);
        return _next++;
    });

    /// <summary>A stream that hands back at most <c>chunk</c> bytes per read, as a slow disk or
    /// a pipe can, so a scan has to cope with a prefix split across two reads.</summary>
    private sealed class TrickleStream(byte[] content, int chunk) : MemoryStream(content)
    {
        public override int Read(byte[] buffer, int offset, int count)
            => base.Read(buffer, offset, Math.Min(count, chunk));

        public override int Read(Span<byte> buffer)
            => base.Read(buffer[..Math.Min(buffer.Length, chunk)]);
    }
}
