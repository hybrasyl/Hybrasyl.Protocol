using System.IO;
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

        var ok = codec.TryDecodeServer(wire, out var packet, out var consumed);

        ok.Should().BeTrue();
        consumed.Should().Be(wire.Length);
        packet.Should().BeOfType<TestPingPacket>().Which.Nonce.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void EncodeClient_ThenDecode_RoundTripsNativeExtensionPacket()
    {
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeClient(new TestPongPacket { Value = 0x2A }, Dialect.V1);

        var ok = codec.TryDecodeClient(wire, out var packet, out _);

        ok.Should().BeTrue();
        packet.Should().BeOfType<TestPongPacket>().Which.Value.Should().Be((byte)0x2A);
    }

    [Fact]
    public void ReplacementOpcode_RoundTripsInExtensionSpace()
    {
        // The "0xB0 ... 0x15 replacement" case: retail 0x15 re-shaped, carried at 0x0015.
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeServer(new TestUpgradedMapInfoPacket { WidenedField = 0x0102 },
            Dialect.V1);

        codec.TryDecodeServer(wire, out var packet, out _).Should().BeTrue();
        packet.Should().BeOfType<TestUpgradedMapInfoPacket>()
            .Which.WidenedField.Should().Be((ushort)0x0102);
        packet!.Opcode.Should().Be((ushort)0x0015);
    }

    [Fact]
    public void Codec_RegistersOnlyDeclaredExtensionPackets_NotRetail()
    {
        // The default codec composes no retail packets — un-migrated retail travels as literal
        // 0xAA frames on DALib's path, never through the extension codec.
        var empty = new ExtensionCodec();
        empty.RegisteredServerOpcodeCount.Should().Be(0);
        empty.RegisteredClientOpcodeCount.Should().Be(0);

        var withTests = CodecWithTestPackets();
        withTests.RegisteredServerOpcodeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Decode_UnregisteredOpcode_Throws()
    {
        var codec = CodecWithTestPackets();
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
