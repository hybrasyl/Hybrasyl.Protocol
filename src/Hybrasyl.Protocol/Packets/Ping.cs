using System;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The extension-native liveness probe, travelling either direction: <c>[u64 token]</c>. The
///     peer answers with a <see cref="Pong" /> echoing the token verbatim. The token is opaque to
///     the receiver - a sender typically uses its own monotonic clock ticks, so RTT falls out of
///     the echo with no bookkeeping and no wire timestamps or clock sync.
/// </summary>
/// <remarks>
///     Replaces retail's asymmetric <c>0x45</c>/<c>0x75</c> heartbeats, which are not carried
///     into the dialect. Interval and timeout policy are the consumer's concern.
/// </remarks>
/// <param name="Token">The opaque probe token the peer must echo.</param>
[ExtensionClientOpcode(ExtensionOpcodes.Ping, Dialect.V1)]
[ExtensionServerOpcode(ExtensionOpcodes.Ping, Dialect.V1)]
public sealed record Ping(ulong Token) : ExtensionPacket
{
    /// <inheritdoc />
    public override ushort Opcode => ExtensionOpcodes.Ping;

    /// <inheritdoc />
    public override void WriteBody(IPacketWriter writer)
    {
        // DALib's wire surface is u32-max (retail never needed wider); a u64 is two BE halves.
        writer.WriteUInt32((uint)(Token >> 32));
        writer.WriteUInt32((uint)Token);
    }

    /// <summary>Parses a <see cref="Ping" /> body.</summary>
    public static Ping Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new Ping(((ulong)reader.ReadUInt32() << 32) | reader.ReadUInt32());
    }
}
