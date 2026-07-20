using System.IO;
using System.Reflection;
using DALib.Networking.Packets.Server;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Framing;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

public class ExtensionCodecTests
{
    private static ExtensionCodec CodecWithTestPackets() =>
        new([typeof(TestPingPacket).Assembly]);

    [Fact]
    public void EncodeServer_ThenDecode_RoundTripsNativeExtensionPacket()
    {
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeServer(new TestPingPacket { Nonce = 0xDEADBEEF }, Dialect.V1);

        var ok = codec.TryDecodeServer(wire, out var decoded, out var consumed);

        ok.Should().BeTrue();
        consumed.Should().Be(wire.Length);
        decoded.IsExtension.Should().BeTrue();
        decoded.Extension.Should().BeOfType<TestPingPacket>()
            .Which.Nonce.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void EncodeClient_ThenDecode_RoundTripsNativeExtensionPacket()
    {
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeClient(new TestPongPacket { Value = 0x2A }, Dialect.V1);

        var ok = codec.TryDecodeClient(wire, out var decoded, out _);

        ok.Should().BeTrue();
        decoded.IsExtension.Should().BeTrue();
        decoded.Extension.Should().BeOfType<TestPongPacket>().Which.Value.Should().Be((byte)0x2A);
    }

    [Fact]
    public void RetailPacket_ComposesAndRoundTripsOverExtensionFraming()
    {
        // A retail DALib packet, unchanged since retail, is available in extension framing at its
        // zero-extended opcode (introduced at 0xAA, resolves under a v1 0xB0 connection).
        var codec = new ExtensionCodec();
        var wire = codec.EncodeRetailServer(new RefreshPacket { Padding = [0x01, 0x02] }, Dialect.V1);

        var ok = codec.TryDecodeServer(wire, out var decoded, out _);

        ok.Should().BeTrue();
        decoded.IsRetail.Should().BeTrue();
        decoded.Opcode.Should().Be((ushort)0x0022);
        decoded.Retail.Should().BeOfType<RefreshPacket>().Which.Padding.Should().Equal(0x01, 0x02);
    }

    [Fact]
    public void DefaultCodec_ComposesDALibServerPackets()
    {
        var codec = new ExtensionCodec();

        codec.RegisteredServerOpcodeCount.Should().BeGreaterThan(0);
        codec.RegisteredClientOpcodeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Decode_UnregisteredOpcode_Throws()
    {
        var codec = new ExtensionCodec();
        var wire = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0xFFFF, new byte[] { 0x00 });

        var act = () => codec.TryDecodeServer(wire, out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*No registered S->C*0xFFFF*");
    }

    [Fact]
    public void Decode_PartialBuffer_ReturnsFalse()
    {
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeServer(new TestPingPacket { Nonce = 1 }, Dialect.V1);

        var ok = codec.TryDecodeServer(wire.AsMemory(0, wire.Length - 1), out _, out var consumed);

        ok.Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void FrameRouter_ClassifiesByFirstByte()
    {
        FrameRouter.Peek(ReadOnlySpan<byte>.Empty).Should().Be(FrameKind.NeedMoreData);
        FrameRouter.Peek(new byte[] { 0xAA, 0x00, 0x03 }).Should().Be(FrameKind.Retail);

        var extensionFrame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, new byte[] { 0x01 });
        FrameRouter.Peek(extensionFrame).Should().Be(FrameKind.Extension);
    }
}
