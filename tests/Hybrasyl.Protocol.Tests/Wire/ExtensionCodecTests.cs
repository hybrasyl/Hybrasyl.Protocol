using System.IO;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Framing;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

public class ExtensionCodecTests
{
    private static ExtensionCodec CodecWithTestPackets() =>
        new([typeof(TestServerNoncePacket).Assembly]);

    [Fact]
    public void EncodeServer_ThenDecode_RoundTripsNativeExtensionPacket()
    {
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeServer(new TestServerNoncePacket { Nonce = 0xDEADBEEF }, Dialect.V1);

        var ok = codec.TryDecodeServer(wire, out var packet, out var consumed);

        ok.Should().BeTrue();
        consumed.Should().Be(wire.Length);
        packet.Should().BeOfType<TestServerNoncePacket>().Which.Nonce.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void EncodeClient_ThenDecode_RoundTripsNativeExtensionPacket()
    {
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeClient(new TestClientBytePacket { Value = 0x2A }, Dialect.V1);

        var ok = codec.TryDecodeClient(wire, out var packet, out _);

        ok.Should().BeTrue();
        packet.Should().BeOfType<TestClientBytePacket>().Which.Value.Should().Be((byte)0x2A);
    }

    [Fact]
    public void TryDecode_WithMatchingExpectedDialect_Decodes()
    {
        var codec = CodecWithTestPackets();
        var wire = codec.EncodeClient(new TestClientBytePacket { Value = 0x2A }, Dialect.V1);

        var ok = codec.TryDecodeClient(wire, out var packet, out _,
            expectedDialect: Dialect.V1);

        ok.Should().BeTrue();
        packet.Should().BeOfType<TestClientBytePacket>();
    }

    [Fact]
    public void TryDecode_WithMismatchedExpectedDialect_Throws()
    {
        // A connection negotiated at one dialect must reject frames stamped with another:
        // resolution keys on the frame's own signature, so accepting it would grant shapes
        // the negotiation never did once a later dialect exists.
        var codec = CodecWithTestPackets();
        var wire = ExtensionFrameCodec.WriteFrame(0xB1,
            new TestClientBytePacket { Value = 0x2A }.Opcode,
            new TestClientBytePacket { Value = 0x2A }.ToBody());

        var act = () => codec.TryDecodeClient(wire, out _, out _,
            expectedDialect: Dialect.V1);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*does not match the negotiated dialect*");
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
        // 0xAA frames on DALib's path, never through the extension codec. Only sreang's own
        // declared packets register: Ping and Pong, each in both directions.
        var sreangOnly = new ExtensionCodec();
        sreangOnly.RegisteredServerOpcodeCount.Should().Be(2);
        sreangOnly.RegisteredClientOpcodeCount.Should().Be(2);

        var withTests = CodecWithTestPackets();
        withTests.RegisteredServerOpcodeCount.Should().BeGreaterThan(2);
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
        var wire = codec.EncodeServer(new TestServerNoncePacket { Nonce = 1 }, Dialect.V1);

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
