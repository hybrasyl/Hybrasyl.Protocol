using System;
using System.IO;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The first server-to-client message inside the established TLS channel: the contiguous range
///     of dialect signatures the server supports, <c>[u8 minSignature][u8 maxSignature]</c>.
///     Because it rides inside TLS, its integrity is protected and there is no plaintext downgrade
///     surface - the range is deliberately never sent in the clear (not in the <c>0x7E</c> marker).
/// </summary>
/// <remarks>
///     Raising <see cref="MinSignature" /> (the floor) retires old dialects permanently. A client
///     resolves its single supported dialect against this range; the three-connection-mode
///     selection (in-range dialect, below-floor <c>0xAA</c>-over-TLS, retail) is the version-policy
///     layer's concern, not this message's.
/// </remarks>
/// <param name="MinSignature">The lowest supported dialect signature (the floor).</param>
/// <param name="MaxSignature">The highest supported dialect signature (the ceiling).</param>
public readonly record struct DialectOffer(byte MinSignature, byte MaxSignature)
{
    /// <summary>Constructs an offer from typed dialects.</summary>
    public DialectOffer(Dialect min, Dialect max) : this((byte)min, (byte)max) { }

    /// <summary>The floor as a <see cref="Dialect" />.</summary>
    public Dialect Min => (Dialect)MinSignature;

    /// <summary>The ceiling as a <see cref="Dialect" />.</summary>
    public Dialect Max => (Dialect)MaxSignature;

    /// <summary>True if <paramref name="signature" /> falls within the offered range.</summary>
    public bool Contains(byte signature) =>
        signature >= MinSignature && signature <= MaxSignature;

    /// <summary>True if <paramref name="dialect" /> falls within the offered range.</summary>
    public bool Contains(Dialect dialect) => Contains((byte)dialect);

    /// <summary>Serialises the 2-byte message.</summary>
    public byte[] ToBytes() => [MinSignature, MaxSignature];

    /// <summary>
    ///     Attempts to read a <see cref="DialectOffer" /> from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if the 2-byte message was present; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The signatures are not valid dialect signatures, or
    ///     the floor exceeds the ceiling.</exception>
    public static bool TryRead(ReadOnlyMemory<byte> buffer, out DialectOffer offer, out int bytesConsumed)
    {
        offer = default;
        bytesConsumed = 0;

        if (buffer.Length < 2)
            return false;

        var span = buffer.Span;
        var min = span[0];
        var max = span[1];

        if (!ExtensionFrame.IsDialectSignature(min) || !ExtensionFrame.IsDialectSignature(max))
            throw new InvalidDataException(
                $"DialectOffer signatures out of range: 0x{min:X2}..0x{max:X2} " +
                $"(valid 0x{ExtensionFrame.MinSignature:X2}..0x{ExtensionFrame.MaxSignature:X2}).");

        if (min > max)
            throw new InvalidDataException(
                $"DialectOffer floor 0x{min:X2} exceeds ceiling 0x{max:X2}.");

        offer = new DialectOffer(min, max);
        bytesConsumed = 2;

        return true;
    }
}
