using System.Collections.Generic;
using System.IO;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Framing;
using Hybrasyl.Protocol.Negotiation;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

public class ExtensionCodecTests
{
    [Fact]
    public void EncodeServer_ThenDecode_RoundTripsNativeExtensionPacket()
    {
        var codec = TestCodec.WithTestPackets();
        var wire = codec.EncodeServer(new TestServerNoncePacket { Nonce = 0xDEADBEEF }, Dialect.V1);

        var ok = codec.TryDecodeServer(wire, out var packet, out var consumed, Dialect.V1);

        ok.Should().BeTrue();
        consumed.Should().Be(wire.Length);
        packet.Should().BeOfType<TestServerNoncePacket>().Which.Nonce.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void EncodeClient_ThenDecode_RoundTripsNativeExtensionPacket()
    {
        var codec = TestCodec.WithTestPackets();
        var wire = codec.EncodeClient(new TestClientBytePacket { Value = 0x2A }, Dialect.V1);

        var ok = codec.TryDecodeClient(wire, out var packet, out _, Dialect.V1);

        ok.Should().BeTrue();
        packet.Should().BeOfType<TestClientBytePacket>().Which.Value.Should().Be((byte)0x2A);
    }

    [Fact]
    public void TryDecode_EnforcesTheExpectedDialect_WithItsOwnPositiveControl()
    {
        // A connection negotiated at one dialect must reject frames stamped with another:
        // resolution keys on the frame's own dialect, so accepting it would grant shapes the
        // negotiation never did once a later dialect exists.
        //
        // Both halves live in one test deliberately. The refusal is only evidence if the same
        // bytes, differing *only* in the stamped dialect, are accepted — otherwise a codec that
        // rejected everything would pass.
        var codec = TestCodec.WithTestPackets();
        var packet = new TestClientBytePacket { Value = 0x2A };

        var negotiated = ExtensionFrameCodec.WriteFrame(Dialect.V1, packet.Opcode, packet.ToBody());
        var foreign = ExtensionFrameCodec.WriteFrame(0xB1, packet.Opcode, packet.ToBody());
        foreign.Length.Should().Be(negotiated.Length, "the two differ only in the dialect byte");

        codec.TryDecodeClient(negotiated, out var decoded, out _, Dialect.V1).Should().BeTrue();
        decoded.Should().BeOfType<TestClientBytePacket>().Which.Value.Should().Be((byte)0x2A);

        var act = () => codec.TryDecodeClient(foreign, out _, out _, Dialect.V1);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*does not match the negotiated dialect*");
    }

    [Fact]
    public void ReplacementOpcode_RoundTripsInExtensionSpace()
    {
        // The "0xB0 ... 0x15 replacement" case: retail 0x15 re-shaped, carried at 0x0015.
        var codec = TestCodec.WithTestPackets();
        var wire = codec.EncodeServer(new TestUpgradedMapInfoPacket { WidenedField = 0x0102 },
            Dialect.V1);

        codec.TryDecodeServer(wire, out var packet, out _, Dialect.V1).Should().BeTrue();
        packet.Should().BeOfType<TestUpgradedMapInfoPacket>()
            .Which.WidenedField.Should().Be((ushort)0x0102);
        packet!.Opcode.Should().Be((ushort)0x0015);
    }

    [Fact]
    public void Codec_RegistersOnlyDeclaredExtensionPackets_NotRetail()
    {
        // The default codec composes no retail packets — un-migrated retail travels as literal
        // 0xAA frames on DALib's path, never through the extension codec. Only sreang's own
        // declared packets register: ClientEcho and ServerEcho, each in both directions.
        var sreangOnly = new ExtensionCodec();
        sreangOnly.RegisteredServerOpcodeCount.Should().Be(2);
        sreangOnly.RegisteredClientOpcodeCount.Should().Be(2);

        var withTests = TestCodec.WithTestPackets();
        withTests.RegisteredServerOpcodeCount.Should().BeGreaterThan(2);
    }

    [Fact]
    public void Decode_UnregisteredOpcode_Throws()
    {
        var codec = TestCodec.WithTestPackets();
        var wire = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0xFFFF, new byte[] { 0x00 });

        var act = () => codec.TryDecodeServer(wire, out _, out _, Dialect.V1);

        act.Should().Throw<InvalidDataException>().WithMessage("*No registered S->C*0xFFFF*");
    }

    [Fact]
    public void Decode_PartialBuffer_ReturnsFalse()
    {
        var codec = TestCodec.WithTestPackets();
        var wire = codec.EncodeServer(new TestServerNoncePacket { Nonce = 1 }, Dialect.V1);

        var ok = codec.TryDecodeServer(wire.AsMemory(0, wire.Length - 1), out _, out var consumed,
            Dialect.V1);

        ok.Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void Encode_ShapeNotResolvedByTheStampedDialect_Throws()
    {
        // 0x0220 is TestShapeIntroducedAtV1 at 0xB0 and TestShapeReplacedAtB1 at 0xB1. Stamping the v1 shape as 0xB1 puts a one-byte body on
        // the wire that the peer resolves to the four-byte shape - so the defect surfaces as
        // garbled fields, never as a protocol error, unless the send site refuses it.
        var codec = TestCodec.WithTestPackets();

        var act = () => codec.EncodeServer(new TestShapeIntroducedAtV1 { Narrow = 0x2A },
            (Dialect)0xB1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestShapeIntroducedAtV1*0xB1*resolves to*TestShapeReplacedAtB1*");
    }

    [Fact]
    public void Encode_NewerShapeStampedWithAnOlderDialect_Throws()
    {
        // The converse direction: a shape its dialect introduced, stamped with a dialect that
        // predates it. Resolution there selects the older type.
        var codec = TestCodec.WithTestPackets();

        var act = () => codec.EncodeServer(new TestShapeReplacedAtB1 { Widened = 0xDEADBEEF },
            Dialect.V1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestShapeReplacedAtB1*0xB0*resolves to*TestShapeIntroducedAtV1*");
    }

    [Fact]
    public void Encode_EachShapeAtItsOwnDialect_RoundTrips()
    {
        // The positive control for the two refusals above: the check must accept the correct
        // pairings, or it would be indistinguishable from a blanket refusal.
        var codec = TestCodec.WithTestPackets();

        var v1Wire = codec.EncodeServer(new TestShapeIntroducedAtV1 { Narrow = 0x2A }, Dialect.V1);
        codec.TryDecodeServer(v1Wire, out var v1Packet, out _, Dialect.V1).Should().BeTrue();
        v1Packet.Should().BeOfType<TestShapeIntroducedAtV1>().Which.Narrow.Should().Be((byte)0x2A);

        var b1Wire = codec.EncodeServer(new TestShapeReplacedAtB1 { Widened = 0xDEADBEEF },
            (Dialect)0xB1);
        codec.TryDecodeServer(b1Wire, out var b1Packet, out _, (Dialect)0xB1).Should().BeTrue();
        b1Packet.Should().BeOfType<TestShapeReplacedAtB1>().Which.Widened.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void Encode_OpcodePropertyContradictingItsAttribute_Throws()
    {
        // The attribute registers 0x0230; the property reports 0x0200. Nothing else in the
        // library compares the two, so the frame would go out claiming to be a nonce packet.
        var codec = TestCodec.WithTestPackets();

        var act = () => codec.EncodeServer(new TestOpcodeContradictsAttributePacket(), Dialect.V1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*resolves to*TestServerNoncePacket*");
    }

    [Fact]
    public void Encode_UnregisteredPacket_Throws()
    {
        var codec = TestCodec.WithTestPackets();

        var act = () => codec.EncodeServer(new TestUnregisteredPacket(), Dialect.V1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no S->C extension packet is registered for opcode 0x0240*");
    }

    [Fact]
    public void Decode_MismatchedDialect_ThrowsOnHeaderAloneWithoutAwaitingBody()
    {
        // The dialect check is decidable from the 8-byte header, so it must not cost cap-sized
        // buffering first. Header only, claiming a near-cap body that never arrives.
        var codec = TestCodec.WithTestPackets();
        var header = new byte[] { 0x00, 0x0F, 0x00, 0x00, 0xB1, 0x02, 0x20, 0x00 };

        var act = () => codec.TryDecodeServer(header, out _, out _, Dialect.V1);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*does not match the negotiated dialect*");
    }

    [Fact]
    public void FrameRouter_ClassifiesByFirstByte()
    {
        FrameRouter.Peek(ReadOnlySpan<byte>.Empty).Should().Be(FrameKind.NeedMoreData);
        FrameRouter.Peek(new byte[] { 0xAA, 0x00, 0x03 }).Should().Be(FrameKind.Retail);

        var extensionFrame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, new byte[] { 0x01 });
        FrameRouter.Peek(extensionFrame).Should().Be(FrameKind.Extension);

        // Real messages, not hand-built bytes: the router must agree with what the writers emit.
        FrameRouter.Peek(new DialectOffer(Dialect.V1, Dialect.V1).ToBytes())
            .Should().Be(FrameKind.Negotiation);
        FrameRouter.Peek(new DialectChoice(Dialect.V1, "v").ToBytes())
            .Should().Be(FrameKind.Negotiation);
    }

    [Fact]
    public void FrameRouter_SeparatesAllThreeKindsOnOneStream()
    {
        // The below-floor case puts negotiation, retail and extension traffic on one TLS stream,
        // so byte 0 alone has to keep them apart with no sequence state.
        var stream = new List<byte>();
        stream.AddRange(new DialectOffer(Dialect.V1, Dialect.V1).ToBytes());
        stream.AddRange(new byte[] { 0xAA, 0x00, 0x03, 0x10 });
        stream.AddRange(ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, new byte[8]));

        var buffer = stream.ToArray().AsMemory();

        FrameRouter.Peek(buffer.Span).Should().Be(FrameKind.Negotiation);
        NegotiationEnvelope.TryRead(buffer, out _, out _, out var consumed).Should().BeTrue();

        buffer = buffer[consumed..];
        FrameRouter.Peek(buffer.Span).Should().Be(FrameKind.Retail);

        buffer = buffer[4..];
        FrameRouter.Peek(buffer.Span).Should().Be(FrameKind.Extension);
    }
}
