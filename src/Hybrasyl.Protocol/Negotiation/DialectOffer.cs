using System;
using System.IO;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The first server-to-client message inside the established TLS channel: the contiguous range
///     of dialects the server supports, <c>[u8 minDialect][u8 maxDialect]</c>.
///     Because it rides inside TLS, its integrity is protected and there is no plaintext downgrade
///     surface - the range is deliberately never sent in the clear (not in the <c>0x7E</c> marker).
/// </summary>
/// <remarks>
///     Raising <see cref="MinDialect" /> (the floor) retires old dialects permanently. A client
///     resolves its single supported dialect against this range; the three-connection-mode
///     selection (in-range dialect, below-floor <c>0xAA</c>-over-TLS, retail) is the version-policy
///     layer's concern, not this message's.
/// </remarks>
/// <param name="MinDialect">The lowest supported dialect (the floor).</param>
/// <param name="MaxDialect">The highest supported dialect (the ceiling).</param>
public readonly record struct DialectOffer(byte MinDialect, byte MaxDialect)
{
    /// <summary>The exact payload size inside the envelope: floor and ceiling.</summary>
    public const int PayloadLength = 2;

    /// <summary>Constructs an offer from typed dialects.</summary>
    public DialectOffer(Dialect min, Dialect max) : this((byte)min, (byte)max) { }

    /// <summary>The floor as a <see cref="Dialect" />.</summary>
    public Dialect Min => (Dialect)MinDialect;

    /// <summary>The ceiling as a <see cref="Dialect" />.</summary>
    public Dialect Max => (Dialect)MaxDialect;

    /// <summary>True if <paramref name="dialect" /> falls within the offered range.</summary>
    public bool Contains(byte dialect) =>
        dialect >= MinDialect && dialect <= MaxDialect;

    /// <summary>True if <paramref name="dialect" /> falls within the offered range.</summary>
    public bool Contains(Dialect dialect) => Contains((byte)dialect);

    /// <summary>
    ///     Serialises the message, envelope included:
    ///     <c>[0xFF][u16 length][0x00 type][u8 min][u8 max]</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">
    ///     The range is one a peer's <see cref="TryRead" /> would reject: a dialect outside
    ///     <c>0xB0</c>..<c>0xFE</c>, or a floor above the ceiling. Serialisation enforces exactly
    ///     the invariants the reader does, so this end cannot put a message on the wire that its
    ///     own reader would refuse.
    /// </exception>
    public byte[] ToBytes()
    {
        Validate(MinDialect, MaxDialect);

        return NegotiationEnvelope.Write(NegotiationMessageType.DialectOffer,
            [MinDialect, MaxDialect]);
    }

    /// <summary>
    ///     The wire invariants, enforced identically on the way out and on the way in.
    /// </summary>
    private static void Validate(byte min, byte max)
    {
        if (!ExtensionFrame.IsValidDialect(min) || !ExtensionFrame.IsValidDialect(max))
            throw new InvalidDataException(
                $"DialectOffer dialects out of range: 0x{min:X2}..0x{max:X2} " +
                $"(valid 0x{ExtensionFrame.MinDialect:X2}..0x{ExtensionFrame.MaxDialect:X2}).");

        if (min > max)
            throw new InvalidDataException(
                $"DialectOffer floor 0x{min:X2} exceeds ceiling 0x{max:X2}.");
    }

    /// <summary>
    ///     Attempts to read a <see cref="DialectOffer" /> from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if a complete message was present; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The envelope is malformed or carries another message
    ///     type, the payload is not exactly two bytes, the dialects are not valid dialects, or the
    ///     floor exceeds the ceiling.</exception>
    public static bool TryRead(ReadOnlyMemory<byte> buffer, out DialectOffer offer, out int bytesConsumed)
    {
        offer = default;

        if (!NegotiationEnvelope.TryReadPayload(buffer, NegotiationMessageType.DialectOffer,
                out var payload, out bytesConsumed))
            return false;

        // Exact-payload consumption, as for packet bodies: a longer payload is the dangerous
        // direction, since it would parse cleanly while discarding the excess.
        if (payload.Length != PayloadLength)
            throw new InvalidDataException(
                $"DialectOffer payload is {payload.Length} bytes; expected exactly {PayloadLength}.");

        var span = payload.Span;
        var min = span[0];
        var max = span[1];

        Validate(min, max);

        offer = new DialectOffer(min, max);

        return true;
    }
}
