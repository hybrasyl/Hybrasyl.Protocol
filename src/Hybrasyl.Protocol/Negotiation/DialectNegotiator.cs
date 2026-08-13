using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     Server-side outcome of the negotiation: the derived resolution plus the client's raw
///     <see cref="DialectChoice" /> (which carries its version string, for the server's records).
/// </summary>
/// <param name="Resolution">The connection mode both sides derived.</param>
/// <param name="Choice">The client's choice as received.</param>
public readonly record struct ServerNegotiationResult(DialectResolution Resolution, DialectChoice Choice);

/// <summary>
///     Client-side outcome of the negotiation: the derived resolution plus the server's raw
///     <see cref="DialectOffer" />.
/// </summary>
/// <param name="Resolution">The connection mode both sides derived.</param>
/// <param name="Offer">The server's offer as received.</param>
public readonly record struct ClientNegotiationResult(DialectResolution Resolution, DialectOffer Offer);

/// <summary>
///     The opening exchange inside an established TLS channel: the server writes its
///     <see cref="DialectOffer" />, the client answers with a <see cref="DialectChoice" /> carrying
///     its real dialect, and each side derives the same <see cref="DialectResolution" /> from
///     (offer, choice) - the connection mode is never separately signaled on the wire.
/// </summary>
/// <remarks>
///     Stream-agnostic (any duplex <see cref="Stream" />, in practice the <c>SslStream</c> the
///     consumer constructed). Reads exactly each message's bytes and never past them, so a client
///     may pipeline frames immediately after its choice without loss.
/// </remarks>
public static class DialectNegotiator
{
    /// <summary>
    ///     Runs the server side: sends the offer derived from <paramref name="policy" />, reads the
    ///     client's choice, and resolves the connection mode.
    /// </summary>
    /// <exception cref="EndOfStreamException">The peer closed mid-negotiation.</exception>
    /// <exception cref="InvalidDataException">The choice carried an invalid dialect.</exception>
    public static async Task<ServerNegotiationResult> NegotiateAsServerAsync(
        Stream stream,
        ServerDialectPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await stream.WriteAsync(policy.ToOffer().ToBytes(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var choice = await ReadChoiceAsync(stream, cancellationToken).ConfigureAwait(false);

        return new ServerNegotiationResult(policy.Resolve(choice.Chosen), choice);
    }

    /// <summary>
    ///     Runs the client side: reads the server's offer, sends this client's choice (always its
    ///     real dialect, even when outside the offer), and resolves the connection mode.
    /// </summary>
    /// <exception cref="EndOfStreamException">The peer closed mid-negotiation.</exception>
    /// <exception cref="InvalidDataException">The offer was malformed.</exception>
    public static async Task<ClientNegotiationResult> NegotiateAsClientAsync(
        Stream stream,
        ClientDialectPolicy policy,
        string clientVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(clientVersion);

        var offerBytes = await ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
        DialectOffer.TryRead(offerBytes, out var offer, out _);

        var choice = new DialectChoice(policy.Supported, clientVersion);
        await stream.WriteAsync(choice.ToBytes(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        return new ClientNegotiationResult(policy.Resolve(offer), offer);
    }

    private static async Task<DialectChoice> ReadChoiceAsync(Stream stream, CancellationToken cancellationToken)
    {
        var message = await ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
        DialectChoice.TryRead(message, out var choice, out _);

        return choice;
    }

    /// <summary>
    ///     Reads exactly one negotiation message: the marker and length field, then exactly
    ///     <c>length</c> more bytes. Never reads past the message, so a peer may pipeline frames
    ///     immediately behind it.
    /// </summary>
    private static async Task<byte[]> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var message = new byte[NegotiationEnvelope.PrefixLength];
        await stream.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);

        // Checked before the length is trusted: a stream that is not speaking this protocol would
        // otherwise have its bytes read as a length, and we would block for a body that never comes.
        if (message[0] != NegotiationEnvelope.Marker)
            throw new InvalidDataException(
                $"Negotiation message starts 0x{message[0]:X2}; expected the " +
                $"0x{NegotiationEnvelope.Marker:X2} marker.");

        var lengthValue = BinaryPrimitives.ReadUInt16BigEndian(
            message.AsSpan(NegotiationEnvelope.MarkerLength, NegotiationEnvelope.LengthFieldLength));

        Array.Resize(ref message, NegotiationEnvelope.PrefixLength + lengthValue);
        await stream
            .ReadExactlyAsync(message.AsMemory(NegotiationEnvelope.PrefixLength, lengthValue),
                cancellationToken)
            .ConfigureAwait(false);

        return message;
    }
}
