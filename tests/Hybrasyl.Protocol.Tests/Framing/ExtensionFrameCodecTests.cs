using System.IO;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Tests.Framing;

public class ExtensionFrameCodecTests
{
    [Fact]
    public void WriteThenRead_RoundTripsHeaderAndBody()
    {
        var body = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, body,
            ExtensionFrameFlags.None);

        var ok = ExtensionFrameCodec.TryReadFrame(frame, out var header, out var readBody,
            out var consumed);

        ok.Should().BeTrue();
        header.Signature.Should().Be((byte)Dialect.V1);
        header.Dialect.Should().Be(Dialect.V1);
        header.Opcode.Should().Be((ushort)0x0100);
        header.Flags.Should().Be(ExtensionFrameFlags.None);
        readBody.ToArray().Should().Equal(body);
        consumed.Should().Be(frame.Length);
    }

    [Fact]
    public void WrittenFrame_HasExpectedByteLayout()
    {
        var body = new byte[] { 0xDE, 0xAD };
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0102, body);

        // [u32-BE length=sig+op+flags+body=4+2=6] [sig 0xB0] [u16-BE op 0x0102] [flags 0x00] [body]
        frame.Should().Equal(0x00, 0x00, 0x00, 0x06, 0xB0, 0x01, 0x02, 0x00, 0xDE, 0xAD);
    }

    [Fact]
    public void WrittenFrame_FirstByteIsZero_NeverCollidesWithRetailMarker()
    {
        // The router discriminates on byte 0: retail frames start 0xAA; an extension frame's
        // big-endian length high byte is 0x00 under the size cap. Guards that routing invariant.
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100,
            new byte[] { 0xAA, 0xAA, 0xAA });

        frame[0].Should().Be(0x00);
        frame[0].Should().NotBe(0xAA);
    }

    [Fact]
    public void EmptyBody_RoundTrips()
    {
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0001, ReadOnlySpan<byte>.Empty);

        var ok = ExtensionFrameCodec.TryReadFrame(frame, out var header, out var body,
            out var consumed);

        ok.Should().BeTrue();
        header.Opcode.Should().Be((ushort)0x0001);
        body.Length.Should().Be(0);
        consumed.Should().Be(ExtensionFrame.HeaderLength);
    }

    [Fact]
    public void TryRead_ShorterThanLengthField_ReturnsFalse()
    {
        var buffer = new byte[] { 0x00, 0x00 };

        var ok = ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out var consumed);

        ok.Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryRead_CompleteHeaderButPartialBody_ReturnsFalse()
    {
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100,
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        var truncated = frame.AsMemory(0, frame.Length - 2);

        var ok = ExtensionFrameCodec.TryReadFrame(truncated, out _, out _, out var consumed);

        ok.Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryRead_LengthBelowHeaderMinimum_Throws()
    {
        // length = 3, below the 4-byte sig+opcode+flags minimum.
        var buffer = new byte[] { 0x00, 0x00, 0x00, 0x03, 0xB0, 0x00, 0x00 };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*below the header minimum*");
    }

    [Fact]
    public void MaxFrameSize_MeansTotalWireSize_WritableAtACapIsReadableAtIt()
    {
        // The minimal frame is exactly HeaderLength bytes; written at a cap of HeaderLength
        // it must read back at the same cap. This is the reader/writer symmetry the two
        // checks used to lack (the reader excluded the 4-byte length field).
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, [],
            maxFrameSize: ExtensionFrame.HeaderLength);
        frame.Length.Should().Be(ExtensionFrame.HeaderLength);

        var ok = ExtensionFrameCodec.TryReadFrame(frame, out _, out _, out var consumed,
            maxFrameSize: ExtensionFrame.HeaderLength);

        ok.Should().BeTrue();
        consumed.Should().Be(ExtensionFrame.HeaderLength);
    }

    [Fact]
    public void TryRead_FrameOneByteOverMaxFrameSize_Throws()
    {
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, [0x00]);
        frame.Length.Should().Be(ExtensionFrame.HeaderLength + 1);

        var act = () => ExtensionFrameCodec.TryReadFrame(frame, out _, out _, out _,
            maxFrameSize: ExtensionFrame.HeaderLength);

        act.Should().Throw<InvalidDataException>().WithMessage("*exceeds MaxFrameSize*");
    }

    [Fact]
    public void TryRead_LengthExceedsMaxFrameSize_ThrowsBeforeRequiringBody()
    {
        // Claims a 16 MiB body but supplies only the length field: the guard must fire on the
        // claim alone, without waiting for the (never-arriving) body.
        var buffer = new byte[] { 0x01, 0x00, 0x00, 0x00 };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _,
            maxFrameSize: ExtensionFrame.DefaultMaxFrameSize);

        act.Should().Throw<InvalidDataException>().WithMessage("*exceeds MaxFrameSize*");
    }

    [Fact]
    public void TryRead_InvalidSignature_Throws()
    {
        // Well-formed length, but signature 0xAA is retail, not a dialect signature.
        var buffer = new byte[] { 0x00, 0x00, 0x00, 0x04, 0xAA, 0x00, 0x00, 0x00 };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*not a valid dialect signature*");
    }

    [Fact]
    public void TryRead_DrainsMultipleFramesFromOneBuffer()
    {
        var first = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, new byte[] { 0xAA });
        var second = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0101, new byte[] { 0xBB, 0xCC });
        var combined = new byte[first.Length + second.Length];
        first.CopyTo(combined, 0);
        second.CopyTo(combined, first.Length);

        var buffer = combined.AsMemory();

        ExtensionFrameCodec.TryReadFrame(buffer, out var h1, out var b1, out var c1)
            .Should().BeTrue();
        h1.Opcode.Should().Be((ushort)0x0100);
        b1.ToArray().Should().Equal(0xAA);

        buffer = buffer[c1..];

        ExtensionFrameCodec.TryReadFrame(buffer, out var h2, out var b2, out var c2)
            .Should().BeTrue();
        h2.Opcode.Should().Be((ushort)0x0101);
        b2.ToArray().Should().Equal(0xBB, 0xCC);

        buffer = buffer[c2..];
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public void WriteFrame_InvalidSignature_Throws()
    {
        var act = () => ExtensionFrameCodec.WriteFrame(0xAA, 0x0100, ReadOnlySpan<byte>.Empty);

        act.Should().Throw<InvalidDataException>().WithMessage("*not a valid dialect signature*");
    }

    [Fact]
    public void WriteFrame_BodyExceedingMaxFrameSize_Throws()
    {
        var body = new byte[16];

        var act = () => ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, body, maxFrameSize: 8);

        act.Should().Throw<InvalidDataException>().WithMessage("*exceeds MaxFrameSize*");
    }
}
