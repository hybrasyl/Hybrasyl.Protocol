using Hybrasyl.Protocol.Transport;

namespace Hybrasyl.Protocol.Tests.Transport;

public class TlsProbeTests
{
    [Fact]
    public void EmptyBuffer_NeedsMoreData()
    {
        TlsProbe.Peek([]).Should().Be(InboundKind.NeedMoreData);
    }

    [Fact]
    public void RetailMarker_ClassifiesRetail()
    {
        TlsProbe.Peek([0xAA, 0x00, 0x0B]).Should().Be(InboundKind.Retail);
    }

    [Fact]
    public void TlsHandshakeRecord_ClassifiesTls()
    {
        // A real ClientHello: record type 0x16, version, length...
        TlsProbe.Peek([0x16, 0x03, 0x01]).Should().Be(InboundKind.TlsHandshake);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x10)] // a retail opcode without its 0xAA frame marker
    [InlineData(0xB0)] // a dialect is never byte 0 pre-TLS
    [InlineData(0xFF)]
    public void AnythingElse_IsInvalid(byte first)
    {
        TlsProbe.Peek([first]).Should().Be(InboundKind.Invalid);
    }
}
