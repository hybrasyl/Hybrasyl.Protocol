using System;
using System.Buffers.Binary;
using System.IO;

namespace Hybrasyl.Protocol.Framing;

/// <summary>
///     Reads and writes the raw extension-frame envelope
///     <c>[u32-BE length] [u8 dialect] [u16-BE opcode] [u8 flags] [body]</c> over a byte
///     stream. This is the framing layer only: it carries no crypto (TLS owns that) and knows
///     nothing about packet types (that is the dispatch layer above it).
/// </summary>
public static class ExtensionFrameCodec
{
    /// <summary>
    ///     Attempts to read a single complete frame from the front of <paramref name="buffer" />.
    /// </summary>
    /// <param name="buffer">
    ///     A buffer that may contain zero, one, or more than one frame. Only the first is read.
    /// </param>
    /// <param name="header">The parsed frame header, when the return value is true.</param>
    /// <param name="body">
    ///     The frame body as a zero-copy slice of <paramref name="buffer" />, when the return
    ///     value is true. May be empty for a body-less frame.
    /// </param>
    /// <param name="bytesConsumed">
    ///     The number of bytes the frame occupied, when the return value is true. Advance the
    ///     buffer by this amount and call again to drain further frames.
    /// </param>
    /// <param name="expectedDialect">
    ///     The negotiated dialect's dialect. A frame stamped with anything else is refused:
    ///     resolution keys on the frame's <em>own</em> dialect, so accepting a foreign stamp
    ///     would grant shapes the negotiation never did. An explicit <see langword="null" /> means
    ///     no connection-level expectation, correct only for a caller that genuinely has not
    ///     negotiated yet.
    /// </param>
    /// <param name="maxFrameSize">
    ///     The largest <b>total wire size</b> (length field included) the reader will accept -
    ///     the same meaning the writer enforces, so any frame writable at a cap is readable at
    ///     that cap. An oversized claim is rejected <em>before</em> waiting for or allocating
    ///     the body, so it cannot force unbounded buffering.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if a complete frame was read; <see langword="false" /> if more
    ///     bytes are needed (a partial frame). Never returns false for a malformed frame - those
    ///     throw.
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     The frame is malformed: the length is below the header minimum, the length exceeds
    ///     <paramref name="maxFrameSize" />, the dialect is not a valid dialect, the
    ///     dialect is not <paramref name="expectedDialect" />, or the flags byte sets a
    ///     reserved bit. The last three are decided on the 8-byte header alone, so an invalid
    ///     frame is refused without buffering its claimed body.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="maxFrameSize" /> is not positive.
    /// </exception>
    public static bool TryReadFrame(
        ReadOnlyMemory<byte> buffer,
        out ExtensionFrameHeader header,
        out ReadOnlyMemory<byte> body,
        out int bytesConsumed,
        byte? expectedDialect,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameSize);

        header = default;
        body = default;
        bytesConsumed = 0;

        if (buffer.Length < ExtensionFrame.LengthFieldLength)
            return false;

        var span = buffer.Span;
        var lengthValue = BinaryPrimitives.ReadUInt32BigEndian(span);

        // Validate the claimed length before waiting for or touching the body.
        if (lengthValue < ExtensionFrame.MinLengthValue)
            throw new InvalidDataException(
                $"Frame length {lengthValue} is below the header minimum of " +
                $"{ExtensionFrame.MinLengthValue}.");

        var totalFrameLength = (long)ExtensionFrame.LengthFieldLength + lengthValue;

        if (totalFrameLength > maxFrameSize)
            throw new InvalidDataException(
                $"Frame length {totalFrameLength} exceeds MaxFrameSize {maxFrameSize}.");

        // MinLengthValue covers dialect, opcode and flags, so every legal frame is at least
        // HeaderLength bytes and deciding here cannot stall one.
        if (buffer.Length < ExtensionFrame.HeaderLength)
            return false;

        var dialect = span[ExtensionFrame.LengthFieldLength];
        var opcode = BinaryPrimitives.ReadUInt16BigEndian(
            span.Slice(ExtensionFrame.LengthFieldLength + ExtensionFrame.DialectLength,
                ExtensionFrame.OpcodeLength));
        var flags = (ExtensionFrameFlags)span[
            ExtensionFrame.LengthFieldLength + ExtensionFrame.DialectLength +
            ExtensionFrame.OpcodeLength];

        ValidateHeaderFields(dialect, flags);

        // The negotiated dialect is constant for a connection's life, so a foreign stamp is
        // decidable on the header alone.
        if (expectedDialect is { } expected && dialect != expected)
            throw new InvalidDataException(
                $"Frame dialect 0x{dialect:X2} does not match the negotiated dialect " +
                $"0x{expected:X2}.");

        if (buffer.Length < totalFrameLength)
            return false;

        var bodyLength = (int)lengthValue - ExtensionFrame.HeaderAfterLengthLength;

        header = new ExtensionFrameHeader(dialect, opcode, flags);
        body = buffer.Slice(ExtensionFrame.HeaderLength, bodyLength);
        bytesConsumed = (int)totalFrameLength;

        return true;
    }

    /// <summary>
    ///     Writes a single frame for <paramref name="dialect" />, <paramref name="opcode" />,
    ///     and <paramref name="body" /> into a freshly allocated array.
    /// </summary>
    /// <param name="dialect">
    ///     The dialect to stamp. For a live connection this is the negotiated dialect,
    ///     constant for every steady-state frame.
    /// </param>
    /// <param name="opcode">The <c>u16</c> opcode.</param>
    /// <param name="body">The plaintext body bytes. May be empty.</param>
    /// <param name="flags">
    ///     The per-frame flags. Every bit is reserved in v1, so anything but
    ///     <see cref="ExtensionFrameFlags.None" /> is refused rather than emitted - a reserved
    ///     bit on the wire would bind a meaning the dialect has not allocated.
    /// </param>
    /// <param name="maxFrameSize">
    ///     The largest frame the writer will produce. A body that would exceed this throws, so
    ///     both ends enforce the same bound.
    /// </param>
    /// <exception cref="InvalidDataException">
    ///     <paramref name="dialect" /> is not a valid dialect,
    ///     <paramref name="flags" /> sets a reserved bit, or the resulting frame length would
    ///     exceed <paramref name="maxFrameSize" />.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="maxFrameSize" /> is not positive.
    /// </exception>
    public static byte[] WriteFrame(
        byte dialect,
        ushort opcode,
        ReadOnlySpan<byte> body,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameSize);
        ValidateHeaderFields(dialect, flags);

        var lengthValue = (long)ExtensionFrame.HeaderAfterLengthLength + body.Length;

        if (ExtensionFrame.LengthFieldLength + lengthValue > maxFrameSize)
            throw new InvalidDataException(
                $"Frame length {ExtensionFrame.LengthFieldLength + lengthValue} exceeds " +
                $"MaxFrameSize {maxFrameSize}.");

        var frame = new byte[ExtensionFrame.HeaderLength + body.Length];
        var span = frame.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)lengthValue);
        span[ExtensionFrame.LengthFieldLength] = dialect;
        BinaryPrimitives.WriteUInt16BigEndian(
            span.Slice(ExtensionFrame.LengthFieldLength + ExtensionFrame.DialectLength,
                ExtensionFrame.OpcodeLength),
            opcode);
        span[ExtensionFrame.LengthFieldLength + ExtensionFrame.DialectLength +
            ExtensionFrame.OpcodeLength] = (byte)flags;
        body.CopyTo(span[ExtensionFrame.HeaderLength..]);

        return frame;
    }

    /// <summary>The header-field invariants, enforced identically on read and write.</summary>
    /// <exception cref="InvalidDataException"><paramref name="dialect" /> is not an allocatable
    ///     dialect, or <paramref name="flags" /> sets a reserved bit.</exception>
    private static void ValidateHeaderFields(byte dialect, ExtensionFrameFlags flags)
    {
        if (!ExtensionFrame.IsValidDialect(dialect))
            throw new InvalidDataException(
                $"Frame dialect 0x{dialect:X2} is not a valid dialect " +
                $"(0x{ExtensionFrame.MinDialect:X2}..0x{ExtensionFrame.MaxDialect:X2}).");

        if (!ExtensionFrame.IsDefinedFlags(flags))
            throw new InvalidDataException(
                $"Frame flags 0x{(byte)flags:X2} set reserved bits; only " +
                $"0x{ExtensionFrame.DefinedFlagsMask:X2} is defined and every other bit must " +
                "be zero.");
    }

    /// <summary>
    ///     Convenience overload of <see cref="WriteFrame(byte, ushort, ReadOnlySpan{byte}, ExtensionFrameFlags, int)" />
    ///     that takes a <see cref="Dialect" /> instead of a raw dialect byte.
    /// </summary>
    public static byte[] WriteFrame(
        Dialect dialect,
        ushort opcode,
        ReadOnlySpan<byte> body,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize) =>
        WriteFrame((byte)dialect, opcode, body, flags, maxFrameSize);
}
