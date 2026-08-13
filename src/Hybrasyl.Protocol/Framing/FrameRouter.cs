using System;

namespace Hybrasyl.Protocol.Framing;

/// <summary>What the first byte of a buffer says about how to parse the frame that follows.</summary>
public enum FrameKind
{
    /// <summary>The buffer is empty; read more before routing.</summary>
    NeedMoreData,

    /// <summary>A retail DOOMVAS v1 frame (starts <c>0xAA</c>) - hand to DALib's codec.</summary>
    Retail,

    /// <summary>
    ///     A system-namespace negotiation message (starts <c>0xFF</c>) - hand to
    ///     <see cref="Negotiation.NegotiationEnvelope" />. Connection-level, not a packet.
    /// </summary>
    Negotiation,

    /// <summary>An extended-framing frame - hand to <see cref="Wire.ExtensionCodec" />.</summary>
    Extension,
}

/// <summary>
///     The first-byte router for a consumer read loop. Retail frames begin <c>0xAA</c>;
///     negotiation messages begin <c>0xFF</c>; an extension frame begins with the high byte of its
///     big-endian length, which cannot be either at any realistic size. This lives in the shared
///     library, not DALib (DALib stays the pure retail codec).
/// </summary>
/// <remarks>
///     <para>
///         A connection can carry all three on one stream. Retail frames ride inside TLS in the
///         below-floor "0xAA-over-TLS" mode, and negotiation precedes everything on any upgraded
///         connection - so routing by content rather than by connection flag is what keeps those
///         cases correct without the loop tracking where it is in a sequence.
///     </para>
///     <para>
///         <b>Neither non-collision depends on <see cref="ExtensionFrame.DefaultMaxFrameSize" />.</b>
///         Byte 0 of an extension frame reaches <c>0xAA</c> only at a length of <c>0xAA000000</c>
///         (about 2.66 GiB) and <c>0xFF</c> only at <c>0xFF000000</c> (about 3.98 GiB). Both are
///         expressible in a <c>u32</c>, so the spec makes a cap below <c>0xAA000004</c> normative
///         rather than assuming no one will go there; here the cap is an <c>int</c>, which cannot
///         reach that bound at all. Below a 16 MiB cap byte 0 is exactly <c>0x00</c>; above it,
///         merely some other value that is neither marker. Stated structurally rather than "it is
///         <c>0x00</c> under the cap" because the cap is a deployment knob, and the weaker
///         phrasing makes every change to it look like it needs a re-derivation. Test on the
///         markers, never on <c>== 0x00</c>.
///     </para>
/// </remarks>
public static class FrameRouter
{
    /// <summary>The retail DOOMVAS v1 outer frame marker.</summary>
    public const byte RetailMarker = 0xAA;

    /// <summary>
    ///     The system-namespace marker: connection-level negotiation messages, never a dialect.
    ///     Mirrors <see cref="Negotiation.NegotiationEnvelope.Marker" />.
    /// </summary>
    public const byte NegotiationMarker = 0xFF;

    /// <summary>
    ///     Classifies the message at the front of <paramref name="buffer" /> by its first byte
    ///     without consuming anything.
    /// </summary>
    public static FrameKind Peek(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return FrameKind.NeedMoreData;

        return buffer[0] switch
        {
            RetailMarker => FrameKind.Retail,
            NegotiationMarker => FrameKind.Negotiation,
            _ => FrameKind.Extension,
        };
    }
}
