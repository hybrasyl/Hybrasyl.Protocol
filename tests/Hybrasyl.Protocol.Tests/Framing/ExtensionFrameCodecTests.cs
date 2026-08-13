using System.Buffers.Binary;
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
            out var consumed, expectedDialect: null);

        ok.Should().BeTrue();
        header.DialectByte.Should().Be((byte)Dialect.V1);
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
    public void WrittenFrame_FirstByteIsZero_UnderTheDefaultCap()
    {
        // Note what this does and does not pin: byte 0 is 0x00 only because the default cap is
        // below 16 MiB; the cap-independent routing guarantee is the test below.
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100,
            new byte[] { 0xAA, 0xAA, 0xAA });

        frame[0].Should().Be(0x00);
    }

    [Fact]
    public void ExtensionFrame_NeverRoutesAsRetail_AtAnyExpressibleLength()
    {
        // The structural claim the router actually rests on. Byte 0 is the big-endian length's
        // high byte, so it reaches 0xAA only at a length of 0xAA000000 (~2.66 GiB) — expressible
        // in u32, never carried. Every length below that routes Extension, including the ones
        // above 16 MiB where byte 0 stops being 0x00.
        foreach (var lengthValue in (uint[])
                 [
                     ExtensionFrame.MinLengthValue,   // smallest legal frame
                     0x000FFFFF,                      // just under 1 MiB
                     0x00FFFFFF,                      // just under 16 MiB — byte 0 still 0x00
                     0x01000000,                      // 16 MiB — byte 0 becomes 0x01
                     0x7FFFFFFF,                      // byte 0 = 0x7F
                     0xA9FFFFFF,                      // the last length before byte 0 hits 0xAA
                 ])
        {
            var header = new byte[ExtensionFrame.HeaderLength];
            BinaryPrimitives.WriteUInt32BigEndian(header, lengthValue);
            header[ExtensionFrame.LengthFieldLength] = (byte)Dialect.V1;

            FrameRouter.Peek(header).Should().Be(FrameKind.Extension,
                $"length 0x{lengthValue:X8} has high byte 0x{header[0]:X2}");
        }

        // And the boundary, stated rather than assumed: 0xAA000000 is where it would collide.
        var colliding = new byte[ExtensionFrame.HeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(colliding, 0xAA000000);
        colliding[0].Should().Be(FrameRouter.RetailMarker);
        (0xAA000000L / (1024 * 1024)).Should().BeGreaterThan(2048,
            "the collision point is past 2 GiB, orders of magnitude above any deployment cap");
    }

    [Fact]
    public void EmptyBody_RoundTrips()
    {
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0001, ReadOnlySpan<byte>.Empty);

        var ok = ExtensionFrameCodec.TryReadFrame(frame, out var header, out var body,
            out var consumed, expectedDialect: null);

        ok.Should().BeTrue();
        header.Opcode.Should().Be((ushort)0x0001);
        body.Length.Should().Be(0);
        consumed.Should().Be(ExtensionFrame.HeaderLength);
    }

    [Fact]
    public void TryRead_ShorterThanLengthField_ReturnsFalse()
    {
        var buffer = new byte[] { 0x00, 0x00 };

        var ok = ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out var consumed,
            expectedDialect: null);

        ok.Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryRead_CompleteHeaderButPartialBody_ReturnsFalse()
    {
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100,
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        var truncated = frame.AsMemory(0, frame.Length - 2);

        var ok = ExtensionFrameCodec.TryReadFrame(truncated, out _, out _, out var consumed,
            expectedDialect: null);

        ok.Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void TryRead_LengthBelowHeaderMinimum_Throws()
    {
        // length = 3, below the 4-byte sig+opcode+flags minimum.
        var buffer = new byte[] { 0x00, 0x00, 0x00, 0x03, 0xB0, 0x00, 0x00 };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _,
            expectedDialect: null);

        act.Should().Throw<InvalidDataException>().WithMessage("*below the header minimum*");
    }

    [Fact]
    public void MaxFrameSize_MeansTotalWireSize_WritableAtACapIsReadableAtIt()
    {
        // The minimal frame is exactly HeaderLength bytes; written at a cap of HeaderLength
        // it must read back at the same cap.
        var frame = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, [],
            maxFrameSize: ExtensionFrame.HeaderLength);
        frame.Length.Should().Be(ExtensionFrame.HeaderLength);

        var ok = ExtensionFrameCodec.TryReadFrame(frame, out _, out _, out var consumed,
            expectedDialect: null,
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
            expectedDialect: null,
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
            expectedDialect: null,
            maxFrameSize: ExtensionFrame.DefaultMaxFrameSize);

        act.Should().Throw<InvalidDataException>().WithMessage("*exceeds MaxFrameSize*");
    }

    [Fact]
    public void TryRead_InvalidDialect_Throws()
    {
        // Well-formed length, but 0xAA is retail's marker and never a dialect.
        var buffer = new byte[] { 0x00, 0x00, 0x00, 0x04, 0xAA, 0x00, 0x00, 0x00 };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _,
            expectedDialect: null);

        act.Should().Throw<InvalidDataException>().WithMessage("*not a valid dialect*");
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

        ExtensionFrameCodec.TryReadFrame(buffer, out var h1, out var b1, out var c1,
            expectedDialect: null)
            .Should().BeTrue();
        h1.Opcode.Should().Be((ushort)0x0100);
        b1.ToArray().Should().Equal(0xAA);

        buffer = buffer[c1..];

        ExtensionFrameCodec.TryReadFrame(buffer, out var h2, out var b2, out var c2,
            expectedDialect: null)
            .Should().BeTrue();
        h2.Opcode.Should().Be((ushort)0x0101);
        b2.ToArray().Should().Equal(0xBB, 0xCC);

        buffer = buffer[c2..];
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public void WriteFrame_InvalidDialect_Throws()
    {
        var act = () => ExtensionFrameCodec.WriteFrame(0xAA, 0x0100, ReadOnlySpan<byte>.Empty);

        act.Should().Throw<InvalidDataException>().WithMessage("*not a valid dialect*");
    }

    [Fact]
    public void WriteFrame_BodyExceedingMaxFrameSize_Throws()
    {
        var body = new byte[16];

        var act = () => ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100, body, maxFrameSize: 8);

        act.Should().Throw<InvalidDataException>().WithMessage("*exceeds MaxFrameSize*");
    }

    [Fact]
    public void TryRead_InvalidDialect_ThrowsOnHeaderAloneWithoutAwaitingBody()
    {
        // Eight header bytes claiming a near-cap body, stamped 0xAA. The verdict is
        // available from the header, so it must be delivered there: deferring it until the body
        // arrives buffers up to MaxFrameSize for a frame that can never be accepted.
        var buffer = new byte[]
        {
            0x00, 0x0F, 0x00, 0x00, // length = 983,040 - legal under the cap
            0xAA,                   // retail's marker, never a dialect
            0x01, 0x00,             // opcode
            0x00,                   // flags
        };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _,
            expectedDialect: null);

        act.Should().Throw<InvalidDataException>().WithMessage("*not a valid dialect*");
    }

    [Fact]
    public void TryRead_ReservedFlagBits_ThrowsOnHeaderAloneWithoutAwaitingBody()
    {
        // Same argument as the dialect case: v1 defines no flag bits, so a set bit is decidable
        // from the header and must not buy the sender cap-sized buffering.
        var buffer = new byte[]
        {
            0x00, 0x0F, 0x00, 0x00, // length = 983,040
            0xB0,                   // valid dialect
            0x01, 0x00,             // opcode
            0x01,                   // bit0 - reserved in v1
        };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _,
            expectedDialect: null);

        act.Should().Throw<InvalidDataException>().WithMessage("*reserved bits*");
    }

    [Fact]
    public void TryRead_ReservedFlagBits_ThrowsOnAnOtherwiseCompleteFrame()
    {
        // The writer refuses to produce this, so it is assembled by hand: a well-formed, complete,
        // body-less frame whose only defect is a reserved flag bit.
        var buffer = new byte[] { 0x00, 0x00, 0x00, 0x04, 0xB0, 0x01, 0x00, 0x80 };

        var act = () => ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out _,
            expectedDialect: null);

        act.Should().Throw<InvalidDataException>().WithMessage("*reserved bits*");
    }

    [Fact]
    public void TryRead_PartialHeader_StillReturnsFalseRatherThanThrowing()
    {
        // The positive control for header-first validation: it must not turn "more bytes
        // needed" into a verdict. Seven bytes is one short of the header, and the byte that would
        // land at the dialect offset is not a dialect - so a reader that validated
        // without first checking it had the whole header would throw here.
        var buffer = new byte[] { 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 };

        var ok = ExtensionFrameCodec.TryReadFrame(buffer, out _, out _, out var consumed,
            expectedDialect: null);

        ok.Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void WriteFrame_ReservedFlagBits_Throws()
    {
        var act = () => ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0100,
            ReadOnlySpan<byte>.Empty, (ExtensionFrameFlags)0x01);

        act.Should().Throw<InvalidDataException>().WithMessage("*reserved bits*");
    }
}
