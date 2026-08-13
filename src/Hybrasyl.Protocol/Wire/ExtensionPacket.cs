using System;
using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Abstract base record for a native extension packet whose single shape travels in
///     <em>both</em> directions - typically an exchange whose reply is the same shape as its
///     request, such as <c>ClientEcho</c>. A concrete packet carries <em>both</em>
///     <see cref="ExtensionClientOpcodeAttribute" /> and
///     <see cref="ExtensionServerOpcodeAttribute" /> (same opcode number), registering the one
///     type in each direction's dispatch table.
/// </summary>
public abstract record ExtensionPacket : IExtensionClientPacket, IExtensionServerPacket
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
