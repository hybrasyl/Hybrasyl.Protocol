using DALib.Networking.Wire;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>A native S->C extension packet used across the codec tests.</summary>
[ExtensionServerOpcode(0x0200, Dialect.V1)]
public sealed record TestServerNoncePacket : ExtensionServerPacket
{
    public required uint Nonce { get; init; }

    public override ushort Opcode => 0x0200;

    public override void WriteBody(IPacketWriter writer) => writer.WriteUInt32(Nonce);

    public static TestServerNoncePacket Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new TestServerNoncePacket { Nonce = reader.ReadUInt32() };
    }
}

/// <summary>A native C->S extension packet used across the codec tests.</summary>
[ExtensionClientOpcode(0x0201, Dialect.V1)]
public sealed record TestClientBytePacket : ExtensionClientPacket
{
    public required byte Value { get; init; }

    public override ushort Opcode => 0x0201;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(Value);

    public static TestClientBytePacket Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new TestClientBytePacket { Value = reader.ReadByte() };
    }
}

/// <summary>
///     A replacement of retail opcode 0x15 with an upgraded shape (retail's u8 field widened to
///     u16), carried in the extension space at its zero-extended opcode 0x0015. Demonstrates the
///     "0xB0 ... 0x15 replacement" case.
/// </summary>
[ExtensionServerOpcode(0x0015, Dialect.V1)]
public sealed record TestUpgradedMapInfoPacket : ExtensionServerPacket
{
    public required ushort WidenedField { get; init; }

    public override ushort Opcode => 0x0015;

    public override void WriteBody(IPacketWriter writer) => writer.WriteUInt16(WidenedField);

    public static TestUpgradedMapInfoPacket Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new TestUpgradedMapInfoPacket { WidenedField = reader.ReadUInt16() };
    }
}
