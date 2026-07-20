using System;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Transport;

/// <summary>What the first inbound plaintext byte says about the connection that follows.</summary>
public enum InboundKind
{
    /// <summary>The buffer is empty; read more before classifying.</summary>
    NeedMoreData,

    /// <summary>A retail DOOMVAS v1 frame (starts <c>0xAA</c>) - the connection stays plaintext.</summary>
    Retail,

    /// <summary>A TLS handshake record (starts <c>0x16</c>) - a ClientHello; upgrade via
    ///     <c>SslStream</c>.</summary>
    TlsHandshake,

    /// <summary>Neither retail nor TLS - garbage; fatal to the connection.</summary>
    Invalid,
}

/// <summary>
///     The server-side pre-TLS discriminator: classifies a connection by its first inbound byte.
///     A retail client's every frame starts <c>0xAA</c>; a TLS-capable client answers the
///     <c>0x7E</c> capability marker by opening a TLS handshake, whose first record byte is always
///     <c>0x16</c> (handshake). The pre-TLS twin of <see cref="FrameRouter" />.
/// </summary>
/// <remarks>
///     The STARTTLS clean-buffer discipline applies at the call site: bytes peeked here must be
///     handed to <c>SslStream</c> intact, never consumed into an application buffer - no pre-TLS
///     byte from the peer may survive into the post-TLS session.
/// </remarks>
public static class TlsProbe
{
    /// <summary>The TLS record type of a handshake record - the first byte of a ClientHello.</summary>
    public const byte TlsHandshakeMarker = 0x16;

    /// <summary>
    ///     Classifies the connection by the first byte of <paramref name="buffer" /> without
    ///     consuming anything.
    /// </summary>
    public static InboundKind Peek(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return InboundKind.NeedMoreData;

        return buffer[0] switch
        {
            FrameRouter.RetailMarker => InboundKind.Retail,
            TlsHandshakeMarker => InboundKind.TlsHandshake,
            _ => InboundKind.Invalid,
        };
    }
}
