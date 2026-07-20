using System;
using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Abstract base record for a native server-to-client extension packet. Concrete packets
///     declare <see cref="Opcode" /> and <see cref="WriteBody" /> plus a
///     <c>public static T Parse(ReadOnlySpan&lt;byte&gt;)</c>, and carry an
///     <see cref="ExtensionServerOpcodeAttribute" />.
/// </summary>
public abstract record ExtensionServerPacket : IExtensionServerPacket
{
    /// <inheritdoc />
    public abstract ushort Opcode { get; }

    /// <inheritdoc />
    public abstract void WriteBody(IPacketWriter writer);

    /// <inheritdoc />
    public byte[] ToBody()
    {
        var writer = new PacketWriter();
        WriteBody(writer);

        return writer.ToArray();
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> ToBodyMemory() => ToBody();
}
