using System;
using System.IO;
using System.Text;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The first client-to-server message inside the established TLS channel: the dialect the
///     client chose and its version string, <c>[u8 chosenDialect][string8 clientVersion]</c>
///     (<c>string8</c> = <c>[u8 length][Latin-1 bytes]</c>, matching the wire's string convention).
///     Sent confidentially inside TLS. The client always sends its real dialect, which may fall
///     <em>outside</em> the server's offered range: both sides then derive
///     <see cref="ConnectionMode.RetailOverTls" /> from (offer, choice) — the mode is never
///     separately signaled on the wire.
/// </summary>
/// <param name="ChosenDialect">The dialect the client speaks (not necessarily within the
///     offer).</param>
/// <param name="ClientVersion">The client's version string, for the server's records.</param>
public readonly record struct DialectChoice(byte ChosenDialect, string ClientVersion)
{
    /// <summary>Constructs a choice from a typed dialect.</summary>
    public DialectChoice(Dialect chosen, string clientVersion) : this((byte)chosen, clientVersion) { }

    /// <summary>The chosen dialect as a <see cref="Dialect" />.</summary>
    public Dialect Chosen => (Dialect)ChosenDialect;

    /// <summary>Serialises the message.</summary>
    /// <exception cref="ArgumentNullException"><see cref="ClientVersion" /> is null.</exception>
    /// <exception cref="InvalidDataException">
    ///     <see cref="ChosenDialect" /> is not a valid dialect, or
    ///     <see cref="ClientVersion" /> exceeds 255 bytes. The dialect check mirrors
    ///     <see cref="TryRead" />, so this end cannot emit a message its own reader would refuse.
    /// </exception>
    public byte[] ToBytes()
    {
        ArgumentNullException.ThrowIfNull(ClientVersion);
        ValidateDialect(ChosenDialect);

        var versionBytes = Encoding.Latin1.GetByteCount(ClientVersion);

        if (versionBytes > byte.MaxValue)
            throw new InvalidDataException(
                $"DialectChoice client version is {versionBytes} bytes; the string8 length is a u8 (max 255).");

        var writer = new PacketWriter();
        writer.WriteByte(ChosenDialect);
        writer.WriteString8(ClientVersion);

        return NegotiationEnvelope.Write(NegotiationMessageType.DialectChoice, writer.ToArray());
    }

    /// <summary>
    ///     Attempts to read a <see cref="DialectChoice" /> from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if a complete message was present; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The envelope is malformed or carries another message
    ///     type, the payload does not match the declared version length, or the chosen dialect is
    ///     not a valid dialect.</exception>
    public static bool TryRead(ReadOnlyMemory<byte> buffer, out DialectChoice choice, out int bytesConsumed)
    {
        choice = default;

        if (!NegotiationEnvelope.TryReadPayload(buffer, NegotiationMessageType.DialectChoice,
                out var payload, out bytesConsumed))
            return false;

        var span = payload.Span;

        // The dialect byte and the string8 length byte.
        if (span.Length < 2)
            throw new InvalidDataException(
                $"DialectChoice payload is {span.Length} bytes; it must carry at least a dialect " +
                "and a version length.");

        var chosen = span[0];
        var versionLength = span[1];

        // Exact-payload consumption: the envelope length and the string8 length must agree, or one
        // of them is lying about where the message ends.
        if (span.Length != 2 + versionLength)
            throw new InvalidDataException(
                $"DialectChoice payload is {span.Length} bytes; the declared version length " +
                $"{versionLength} implies {2 + versionLength}.");

        ValidateDialect(chosen);

        var version = Encoding.Latin1.GetString(span.Slice(2, versionLength));

        choice = new DialectChoice(chosen, version);

        return true;
    }

    /// <summary>The dialect invariant, enforced identically on the way out and on the way in.</summary>
    private static void ValidateDialect(byte chosen)
    {
        if (!ExtensionFrame.IsValidDialect(chosen))
            throw new InvalidDataException(
                $"DialectChoice dialect 0x{chosen:X2} is out of range " +
                $"(valid 0x{ExtensionFrame.MinDialect:X2}..0x{ExtensionFrame.MaxDialect:X2}).");
    }
}
