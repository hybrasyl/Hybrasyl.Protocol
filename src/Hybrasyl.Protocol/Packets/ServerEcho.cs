using System;
using System.IO;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The server-initiated liveness exchange: <c>[u64 token]</c>. Sent S-&gt;C as the probe and
///     returned C-&gt;S as the reply, both at <see cref="ExtensionOpcodes.ServerEcho" /> - the
///     number identifies the exchange, and the direction says which half it is.
/// </summary>
/// <remarks>
///     Identical in shape and rules to <see cref="ClientEcho" />; the two exist as separate
///     exchanges so that each side's probe is answered at the number it was sent on. The
///     responder MUST echo <see cref="Token" /> verbatim.
/// </remarks>
/// <param name="Token">The probe token the responder echoes.</param>
[ExtensionClientOpcode(ExtensionOpcodes.ServerEcho, Dialect.V1)]
[ExtensionServerOpcode(ExtensionOpcodes.ServerEcho, Dialect.V1)]
public sealed record ServerEcho(ulong Token) : ExtensionPacket
{
    /// <summary>The exact body size on the wire: one <c>u64</c> token.</summary>
    public const int BodyLength = 8;

    /// <inheritdoc />
    public override ushort Opcode => ExtensionOpcodes.ServerEcho;

    /// <inheritdoc />
    public override void WriteBody(IPacketWriter writer) => writer.WriteUInt64(Token);

    /// <summary>Parses a <see cref="ServerEcho" /> body.</summary>
    /// <exception cref="InvalidDataException"><paramref name="body" /> is not exactly
    ///     <see cref="BodyLength" /> bytes.</exception>
    public static ServerEcho Parse(ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
            throw new InvalidDataException(
                $"ServerEcho body is {body.Length} bytes; expected exactly {BodyLength}.");

        var reader = new PacketReader(body);

        return new ServerEcho(reader.ReadUInt64());
    }
}
