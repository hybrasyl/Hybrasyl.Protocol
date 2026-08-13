using DALib.Networking.Wire;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>Shared construction for the codec tests in this namespace.</summary>
internal static class TestCodec
{
    /// <summary>
    ///     A codec that has discovered this assembly's test packets alongside the library's own.
    ///     Named here rather than in each test class so the seed type appears once - two copies
    ///     drift the moment a second fixture assembly is added.
    /// </summary>
    internal static ExtensionCodec WithTestPackets() =>
        new([typeof(TestServerNoncePacket).Assembly]);
}

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
///     One opcode's shape as introduced in v1, paired with <see cref="TestShapeReplacedAtB1" />.
/// </summary>
/// <remarks>
///     <para>
///         These two exist to make the <em>second dialect</em> real in tests. <see cref="Dialect" />
///         publicly declares only <c>V1</c>, so without a cast every latest-wins property would be
///         untestable end-to-end until the day 0xB1 ships - which is exactly the day the mistakes
///         it guards against become reachable. <c>(Dialect)0xB1</c> is a constant expression and so
///         is legal as an attribute argument; it buys a genuine two-shape opcode now.
///     </para>
///     <para>
///         The two bodies are deliberately <em>different widths</em>, so parsing one as the other
///         is a visible corruption rather than a coincidence that happens to round-trip.
///     </para>
/// </remarks>
[ExtensionServerOpcode(0x0220, Dialect.V1)]
public sealed record TestShapeIntroducedAtV1 : ExtensionServerPacket
{
    public required byte Narrow { get; init; }

    public override ushort Opcode => 0x0220;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(Narrow);

    public static TestShapeIntroducedAtV1 Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new TestShapeIntroducedAtV1 { Narrow = reader.ReadByte() };
    }
}

/// <summary>The same opcode re-shaped at dialect 0xB1. See <see cref="TestShapeIntroducedAtV1" />.</summary>
[ExtensionServerOpcode(0x0220, (Dialect)0xB1)]
public sealed record TestShapeReplacedAtB1 : ExtensionServerPacket
{
    public required uint Widened { get; init; }

    public override ushort Opcode => 0x0220;

    public override void WriteBody(IPacketWriter writer) => writer.WriteUInt32(Widened);

    public static TestShapeReplacedAtB1 Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);

        return new TestShapeReplacedAtB1 { Widened = reader.ReadUInt32() };
    }
}

/// <summary>
///     A packet whose <see cref="IExtensionPacket.Opcode" /> contradicts its registration
///     attribute: registered at 0x0230, but it reports 0x0200 - <see cref="TestServerNoncePacket" />'s
///     number. The property and the attribute are written independently and nothing but the
///     encode-side shape check compares them.
/// </summary>
[ExtensionServerOpcode(0x0230, Dialect.V1)]
public sealed record TestOpcodeContradictsAttributePacket : ExtensionServerPacket
{
    public override ushort Opcode => 0x0200;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(0x00);

    public static TestOpcodeContradictsAttributePacket Parse(ReadOnlySpan<byte> body) => new();
}

/// <summary>
///     A well-formed extension packet that is never registered at all - no opcode attribute, so
///     no peer could resolve a decoder for it.
/// </summary>
public sealed record TestUnregisteredPacket : ExtensionServerPacket
{
    public override ushort Opcode => 0x0240;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(0x00);
}

// The three below are registration-metadata defects. They deliberately carry NO opcode attribute,
// so the assembly scan never sees them - each is fed to a throwaway table in its own test instead.
// Attributed, they would break every codec construction in the suite.

/// <summary>
///     Declares <c>Parse</c> returning a <em>different</em> registered packet type. The dispatch
///     table would record "decoder produces <see cref="TestParseReturnsAnotherTypePacket" />" while
///     the bound method produces a <see cref="TestServerNoncePacket" /> - so the encode-side shape
///     check would certify a record that is false.
/// </summary>
public sealed record TestParseReturnsAnotherTypePacket : ExtensionServerPacket
{
    public override ushort Opcode => 0x0250;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(0x00);

    public static TestServerNoncePacket Parse(ReadOnlySpan<byte> body) => new() { Nonce = 0 };
}

/// <summary>Declares no <c>Parse</c> at all.</summary>
public sealed record TestNoParsePacket : ExtensionServerPacket
{
    public override ushort Opcode => 0x0251;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(0x00);
}

/// <summary>
///     A shape whose introduction dialect is outside <c>0xB0</c>..<c>0xFE</c>. Resolution is
///     "highest introduction &lt;= the frame's dialect", so registered at <c>0x00</c> it would
///     resolve for every frame - including dialects that never contained it.
/// </summary>
public sealed record TestSinceOutOfRangePacket : ExtensionServerPacket
{
    public override ushort Opcode => 0x0252;

    public override void WriteBody(IPacketWriter writer) => writer.WriteByte(0x00);

    public static TestSinceOutOfRangePacket Parse(ReadOnlySpan<byte> body) => new();
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
