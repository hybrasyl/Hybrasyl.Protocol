using System;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The extension-native liveness reply, travelling either direction: <c>[u64 token]</c>,
///     echoing a received <see cref="Ping" />'s token verbatim. A separate opcode rather than a
///     kind-byte variant so the two messages stay individually visible to dispatch and
///     versioning, and may diverge in later dialects.
/// </summary>
/// <param name="Token">The echoed probe token.</param>
[ExtensionClientOpcode(ExtensionOpcodes.Pong, Dialect.V1)]
[ExtensionServerOpcode(ExtensionOpcodes.Pong, Dialect.V1)]
public sealed record Pong(ulong Token) : ExtensionPacket
{
    /// <inheritdoc />
    public override ushort Opcode => ExtensionOpcodes.Pong;

    /// <inheritdoc />
    public override void WriteBody(IPacketWriter writer)
    {
        writer.WriteUInt32((uint)(Token >> 32));
        writer.WriteUInt32((uint)Token);
    }

    /// <summary>Constructs the reply to <paramref name="ping" />.</summary>
    public static Pong For(Ping ping)
    {
        ArgumentNullException.ThrowIfNull(ping);

        return new Pong(ping.Token);
    }

    /// <summary>Parses a <see cref="Pong" /> body.</summary>
    public static Pong Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new Pong(((ulong)reader.ReadUInt32() << 32) | reader.ReadUInt32());
    }
}
