using DALib.Networking.Wire;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>A native S->C extension packet used across the codec tests.</summary>
[ExtensionServerOpcode(0x0100, Dialect.V1)]
public sealed record TestPingPacket : ExtensionServerPacket
{
    public required uint Nonce { get; init; }

    public override ushort Opcode => 0x0100;

    public override void WriteBody(IPacketWriter writer) => writer.WriteUInt32(Nonce);

    public static TestPingPacket Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new TestPingPacket { Nonce = reader.ReadUInt32() };
    }
}

/// <summary>A native C->S extension packet used across the codec tests.</summary>
[ExtensionClientOpcode(0x0101, Dialect.V1)]
public sealed record TestPongPacket : ExtensionClientPacket
{
    public required byte Value { get; init; }

    public override ushort Opcode => 0x0101;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(Value);

    public static TestPongPacket Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new TestPongPacket { Value = reader.ReadByte() };
    }
}
