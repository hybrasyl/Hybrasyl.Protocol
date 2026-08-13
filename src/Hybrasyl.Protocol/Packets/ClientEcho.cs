using System;
using System.IO;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The client-initiated liveness exchange: <c>[u64 token]</c>. Sent C-&gt;S as the probe and
///     returned S-&gt;C as the reply, both at <see cref="ExtensionOpcodes.ClientEcho" /> - the
///     number identifies the exchange, and the direction says which half it is.
/// </summary>
/// <remarks>
///     The responder MUST echo <see cref="Token" /> verbatim. It is opaque to the receiver, so a
///     sender typically uses its own monotonic clock ticks and reads round-trip time straight off
///     the reply, with no wire timestamps and no clock synchronisation. Interval and timeout
///     policy belong to the consumer. See <see cref="ServerEcho" /> for the server-initiated
///     direction.
/// </remarks>
/// <param name="Token">The probe token the responder echoes.</param>
[ExtensionClientOpcode(ExtensionOpcodes.ClientEcho, Dialect.V1)]
[ExtensionServerOpcode(ExtensionOpcodes.ClientEcho, Dialect.V1)]
public sealed record ClientEcho(ulong Token) : ExtensionPacket
{
    /// <summary>The exact body size on the wire: one <c>u64</c> token.</summary>
    public const int BodyLength = 8;

    /// <inheritdoc />
    public override ushort Opcode => ExtensionOpcodes.ClientEcho;

    /// <inheritdoc />
    public override void WriteBody(IPacketWriter writer) => writer.WriteUInt64(Token);

    /// <summary>Parses a <see cref="ClientEcho" /> body.</summary>
    /// <exception cref="InvalidDataException"><paramref name="body" /> is not exactly
    ///     <see cref="BodyLength" /> bytes.</exception>
    public static ClientEcho Parse(ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
            throw new InvalidDataException(
                $"ClientEcho body is {body.Length} bytes; expected exactly {BodyLength}.");

        var reader = new PacketReader(body);

        return new ClientEcho(reader.ReadUInt64());
    }
}
