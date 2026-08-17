using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The <see cref="AttributeBlockFlags.Vitals" /> block of an <see cref="Attributes" /> body:
///     current hit and magic points.
/// </summary>
/// <remarks>
///     Deliberately minimal, and unchanged from retail. This is the block sent on every damage
///     and heal tick; <c>u32</c> is already exact and there is nothing here to widen.
/// </remarks>
/// <param name="Hp">Current hit points.</param>
/// <param name="Mp">Current magic points.</param>
public sealed record AttributesVitals(uint Hp, uint Mp)
{
    /// <summary>The exact size of this block on the wire.</summary>
    public const int BlockLength = 8;

    internal void Write(IPacketWriter writer)
    {
        writer.WriteUInt32(Hp);
        writer.WriteUInt32(Mp);
    }

    internal static AttributesVitals Read(ref PacketReader reader) =>
        new(reader.ReadUInt32(), reader.ReadUInt32());
}
