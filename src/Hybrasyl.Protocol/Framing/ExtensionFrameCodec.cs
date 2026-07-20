using System;
using System.Buffers.Binary;
using System.IO;

namespace Hybrasyl.Protocol.Framing;

/// <summary>
///     Reads and writes the raw extension-frame envelope
///     <c>[u32-BE length] [u8 signature] [u16-BE opcode] [u8 flags] [body]</c> over a byte
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
    /// <param name="maxFrameSize">
    ///     The largest length the reader will accept. A claimed length above this is rejected
    ///     <em>before</em> waiting for or allocating the body, so an oversized claim cannot force
    ///     unbounded buffering.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if a complete frame was read; <see langword="false" /> if more
    ///     bytes are needed (a partial frame). Never returns false for a malformed frame - those
    ///     throw.
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     The frame is malformed: the length is below the header minimum, the length exceeds
    ///     <paramref name="maxFrameSize" />, or the signature is not a valid dialect signature.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="maxFrameSize" /> is not positive.
    /// </exception>
    public static bool TryReadFrame(
        ReadOnlyMemory<byte> buffer,
        out ExtensionFrameHeader header,
        out ReadOnlyMemory<byte> body,
        out int bytesConsumed,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameSize);

        header = default;
        body = default;
        bytesConsumed = 0;

        // Need the length field before we can know the frame boundary.
        if (buffer.Length < ExtensionFrame.LengthFieldLength)
            return false;

        var span = buffer.Span;
        var lengthValue = BinaryPrimitives.ReadUInt32BigEndian(span);

        // Validate the claimed length before waiting for or touching the body. This is the
        // allocation-guard discipline: an oversized claim is fatal, never buffered.
        if (lengthValue < ExtensionFrame.MinLengthValue)
            throw new InvalidDataException(
                $"Frame length {lengthValue} is below the header minimum of " +
                $"{ExtensionFrame.MinLengthValue}.");

        if (lengthValue > (uint)maxFrameSize)
            throw new InvalidDataException(
                $"Frame length {lengthValue} exceeds MaxFrameSize {maxFrameSize}.");

        var totalFrameLength = (long)ExtensionFrame.LengthFieldLength + lengthValue;

        // Header validated, but the whole body hasn't arrived yet.
        if (buffer.Length < totalFrameLength)
            return false;

        var signature = span[ExtensionFrame.LengthFieldLength];

        if (!ExtensionFrame.IsDialectSignature(signature))
            throw new InvalidDataException(
                $"Frame signature 0x{signature:X2} is not a valid dialect signature " +
                $"(0x{ExtensionFrame.MinSignature:X2}..0x{ExtensionFrame.MaxSignature:X2}).");

        var opcode = BinaryPrimitives.ReadUInt16BigEndian(
            span.Slice(ExtensionFrame.LengthFieldLength + ExtensionFrame.SignatureLength,
                ExtensionFrame.OpcodeLength));
        var flags = (ExtensionFrameFlags)span[
            ExtensionFrame.LengthFieldLength + ExtensionFrame.SignatureLength +
            ExtensionFrame.OpcodeLength];

        var bodyLength = (int)lengthValue - ExtensionFrame.HeaderAfterLengthLength;

        header = new ExtensionFrameHeader(signature, opcode, flags);
        body = buffer.Slice(ExtensionFrame.HeaderLength, bodyLength);
        bytesConsumed = (int)totalFrameLength;

        return true;
    }

    /// <summary>
    ///     Writes a single frame for <paramref name="signature" />, <paramref name="opcode" />,
    ///     and <paramref name="body" /> into a freshly allocated array.
    /// </summary>
    /// <param name="signature">
    ///     The dialect signature to stamp. For a live connection this is the negotiated dialect,
    ///     constant for every steady-state frame.
    /// </param>
    /// <param name="opcode">The <c>u16</c> opcode.</param>
    /// <param name="body">The plaintext body bytes. May be empty.</param>
    /// <param name="flags">The per-frame flags. Defaults to <see cref="ExtensionFrameFlags.None" />.</param>
    /// <param name="maxFrameSize">
    ///     The largest frame the writer will produce. A body that would exceed this throws, so
    ///     both ends enforce the same bound.
    /// </param>
    /// <exception cref="InvalidDataException">
    ///     <paramref name="signature" /> is not a valid dialect signature, or the resulting frame
    ///     length would exceed <paramref name="maxFrameSize" />.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="maxFrameSize" /> is not positive.
    /// </exception>
    public static byte[] WriteFrame(
        byte signature,
        ushort opcode,
        ReadOnlySpan<byte> body,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameSize);

        if (!ExtensionFrame.IsDialectSignature(signature))
            throw new InvalidDataException(
                $"Frame signature 0x{signature:X2} is not a valid dialect signature " +
                $"(0x{ExtensionFrame.MinSignature:X2}..0x{ExtensionFrame.MaxSignature:X2}).");

        var lengthValue = (long)ExtensionFrame.HeaderAfterLengthLength + body.Length;

        if (ExtensionFrame.LengthFieldLength + lengthValue > maxFrameSize)
            throw new InvalidDataException(
                $"Frame length {ExtensionFrame.LengthFieldLength + lengthValue} exceeds " +
                $"MaxFrameSize {maxFrameSize}.");

        var frame = new byte[ExtensionFrame.HeaderLength + body.Length];
        var span = frame.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)lengthValue);
        span[ExtensionFrame.LengthFieldLength] = signature;
        BinaryPrimitives.WriteUInt16BigEndian(
            span.Slice(ExtensionFrame.LengthFieldLength + ExtensionFrame.SignatureLength,
                ExtensionFrame.OpcodeLength),
            opcode);
        span[ExtensionFrame.LengthFieldLength + ExtensionFrame.SignatureLength +
            ExtensionFrame.OpcodeLength] = (byte)flags;
        body.CopyTo(span[ExtensionFrame.HeaderLength..]);

        return frame;
    }

    /// <summary>
    ///     Convenience overload of <see cref="WriteFrame(byte, ushort, ReadOnlySpan{byte}, ExtensionFrameFlags, int)" />
    ///     that takes a <see cref="Dialect" /> instead of a raw signature byte.
    /// </summary>
    public static byte[] WriteFrame(
        Dialect dialect,
        ushort opcode,
        ReadOnlySpan<byte> body,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize) =>
        WriteFrame((byte)dialect, opcode, body, flags, maxFrameSize);
}
