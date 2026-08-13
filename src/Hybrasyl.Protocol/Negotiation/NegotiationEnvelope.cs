using System;
using System.Buffers.Binary;
using System.IO;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>The negotiation messages carried under the <c>0xFF</c> system namespace.</summary>
public enum NegotiationMessageType : byte
{
    /// <summary>Server-to-client: the supported dialect range. <see cref="DialectOffer" />.</summary>
    DialectOffer = 0x00,

    /// <summary>Client-to-server: the chosen dialect and client version. <see cref="DialectChoice" />.</summary>
    DialectChoice = 0x01,
}

/// <summary>
///     The envelope every negotiation message travels in:
///     <c>[0xFF marker] [u16-BE length] [u8 type] [payload]</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b><c>0xFF</c> is the system-level namespace.</b> It is not an allocatable dialect, so
///         it can never collide with one, and it makes negotiation traffic self-identifying at
///         byte 0 - the same position everything else inside TLS is routed on. A consumer's read
///         loop dispatches on one byte for all three cases: <c>0xAA</c> retail, <c>0xFF</c>
///         negotiation, anything else an extension frame. Future connection-level messages that
///         are not packets belong here too, under new <see cref="NegotiationMessageType" />
///         values.
///     </para>
///     <para>
///         <b>Why an envelope at all.</b> Negotiation establishes the dialect, so unlike every
///         packet it cannot be re-versioned by the dialect mechanism - the same one-time-choice
///         property the frame's <c>u32</c> length has. The length prefix is what buys an
///         extension path anyway: a reader that does not recognise a message, or that meets a
///         longer payload than it expects, can skip exactly <c>length</c> bytes and stay in sync.
///         Without it these messages could never change.
///     </para>
///     <para>
///         <b>Length is <c>u16</c> deliberately.</b> The frame's <c>u32</c> exists for map blobs
///         and profile images; negotiation carries a range and a version string. 64 KiB is a
///         ceiling this namespace will never approach, and matching the frame here would cost two
///         bytes on every connection to express nothing.
///     </para>
/// </remarks>
public static class NegotiationEnvelope
{
    /// <summary>The system-namespace marker at byte 0.</summary>
    public const byte Marker = 0xFF;

    /// <summary>Size of the <c>u8</c> marker field.</summary>
    public const int MarkerLength = 1;

    /// <summary>Size of the <c>u16</c> length field.</summary>
    public const int LengthFieldLength = 2;

    /// <summary>Size of the <c>u8</c> message-type field.</summary>
    public const int TypeLength = 1;

    /// <summary>Total fixed prefix before the payload: marker + length + type.</summary>
    public const int HeaderLength = MarkerLength + LengthFieldLength + TypeLength;

    /// <summary>
    ///     Bytes read before <c>length</c> is known: marker + length field. A reader takes exactly
    ///     this many, then exactly <c>length</c> more, and so never consumes past the message.
    /// </summary>
    public const int PrefixLength = MarkerLength + LengthFieldLength;

    /// <summary>
    ///     The smallest legal <c>length</c> value: it must cover at least the type byte of a
    ///     payload-less message. <c>length</c> counts everything after the length field, the same
    ///     meaning it has in an extension frame.
    /// </summary>
    public const int MinLengthValue = TypeLength;

    /// <summary>The largest payload this envelope can carry.</summary>
    public const int MaxPayloadLength = ushort.MaxValue - TypeLength;

    /// <summary>Wraps <paramref name="payload" /> in the envelope.</summary>
    /// <exception cref="InvalidDataException"><paramref name="payload" /> exceeds
    ///     <see cref="MaxPayloadLength" />.</exception>
    public static byte[] Write(NegotiationMessageType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadLength)
            throw new InvalidDataException(
                $"Negotiation payload is {payload.Length} bytes; the u16 length field caps it at " +
                $"{MaxPayloadLength}.");

        var message = new byte[HeaderLength + payload.Length];
        var span = message.AsSpan();

        span[0] = Marker;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(MarkerLength, LengthFieldLength),
            (ushort)(TypeLength + payload.Length));
        span[PrefixLength] = (byte)type;
        payload.CopyTo(span[HeaderLength..]);

        return message;
    }

    /// <summary>
    ///     Attempts to read one envelope from the front of <paramref name="buffer" />, returning
    ///     its type and payload as a zero-copy slice.
    /// </summary>
    /// <returns><see langword="true" /> if a complete message was present; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">Byte 0 is not <see cref="Marker" />, or the length
    ///     is below <see cref="MinLengthValue" />.</exception>
    public static bool TryRead(
        ReadOnlyMemory<byte> buffer,
        out NegotiationMessageType type,
        out ReadOnlyMemory<byte> payload,
        out int bytesConsumed)
    {
        type = default;
        payload = default;
        bytesConsumed = 0;

        if (buffer.Length < PrefixLength)
            return false;

        var span = buffer.Span;

        if (span[0] != Marker)
            throw new InvalidDataException(
                $"Negotiation message starts 0x{span[0]:X2}; expected the 0x{Marker:X2} marker.");

        var lengthValue = BinaryPrimitives.ReadUInt16BigEndian(
            span.Slice(MarkerLength, LengthFieldLength));

        if (lengthValue < MinLengthValue)
            throw new InvalidDataException(
                $"Negotiation length {lengthValue} is below the minimum of {MinLengthValue}.");

        var total = PrefixLength + lengthValue;

        if (buffer.Length < total)
            return false;

        type = (NegotiationMessageType)span[PrefixLength];
        payload = buffer.Slice(HeaderLength, lengthValue - TypeLength);
        bytesConsumed = total;

        return true;
    }

    /// <summary>
    ///     Reads the envelope and requires it to carry <paramref name="expected" />, returning the
    ///     payload.
    /// </summary>
    /// <exception cref="InvalidDataException">The envelope is malformed, or carries a different
    ///     message type.</exception>
    public static bool TryReadPayload(
        ReadOnlyMemory<byte> buffer,
        NegotiationMessageType expected,
        out ReadOnlyMemory<byte> payload,
        out int bytesConsumed)
    {
        if (!TryRead(buffer, out var type, out payload, out bytesConsumed))
            return false;

        if (type != expected)
            throw new InvalidDataException(
                $"Negotiation message is type 0x{(byte)type:X2}; expected 0x{(byte)expected:X2} " +
                $"({expected}).");

        return true;
    }
}
