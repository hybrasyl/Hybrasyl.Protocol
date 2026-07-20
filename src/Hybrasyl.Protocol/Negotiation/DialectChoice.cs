using System;
using System.IO;
using System.Text;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The first client-to-server message inside the established TLS channel: the dialect the
///     client chose and its version string, <c>[u8 chosenSignature][string8 clientVersion]</c>
///     (<c>string8</c> = <c>[u8 length][Latin-1 bytes]</c>, matching the wire's string convention).
///     Sent confidentially inside TLS.
/// </summary>
/// <param name="ChosenSignature">The dialect signature the client selected from the offer.</param>
/// <param name="ClientVersion">The client's version string, for the server's records.</param>
public readonly record struct DialectChoice(byte ChosenSignature, string ClientVersion)
{
    /// <summary>Constructs a choice from a typed dialect.</summary>
    public DialectChoice(Dialect chosen, string clientVersion) : this((byte)chosen, clientVersion) { }

    /// <summary>The chosen dialect as a <see cref="Dialect" />.</summary>
    public Dialect Chosen => (Dialect)ChosenSignature;

    /// <summary>Serialises the message.</summary>
    /// <exception cref="ArgumentNullException"><see cref="ClientVersion" /> is null.</exception>
    /// <exception cref="InvalidDataException"><see cref="ClientVersion" /> exceeds 255 bytes.</exception>
    public byte[] ToBytes()
    {
        ArgumentNullException.ThrowIfNull(ClientVersion);

        var versionBytes = Encoding.Latin1.GetByteCount(ClientVersion);

        if (versionBytes > byte.MaxValue)
            throw new InvalidDataException(
                $"DialectChoice client version is {versionBytes} bytes; the string8 length is a u8 (max 255).");

        var writer = new PacketWriter();
        writer.WriteByte(ChosenSignature);
        writer.WriteString8(ClientVersion);

        return writer.ToArray();
    }

    /// <summary>
    ///     Attempts to read a <see cref="DialectChoice" /> from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if a complete message was present; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The chosen signature is not a valid dialect
    ///     signature.</exception>
    public static bool TryRead(ReadOnlyMemory<byte> buffer, out DialectChoice choice, out int bytesConsumed)
    {
        choice = default;
        bytesConsumed = 0;

        var span = buffer.Span;

        // Need at least the signature byte and the string8 length byte.
        if (span.Length < 2)
            return false;

        var chosen = span[0];
        var versionLength = span[1];
        var total = 2 + versionLength;

        if (span.Length < total)
            return false;

        if (!ExtensionFrame.IsDialectSignature(chosen))
            throw new InvalidDataException(
                $"DialectChoice signature 0x{chosen:X2} is out of range " +
                $"(valid 0x{ExtensionFrame.MinSignature:X2}..0x{ExtensionFrame.MaxSignature:X2}).");

        var version = Encoding.Latin1.GetString(span.Slice(2, versionLength));

        choice = new DialectChoice(chosen, version);
        bytesConsumed = total;

        return true;
    }
}
