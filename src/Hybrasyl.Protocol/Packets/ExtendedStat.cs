using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     One record of the <see cref="AttributeBlockFlags.ExtendedStats" /> block:
///     <c>[u16 statId][f64 value]</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>Units never travel on the wire.</b> A multiplier, a probability and a flat rating
///         are all <c>f64</c>; what one means is a property of its <paramref name="StatId" />, and
///         belongs to whatever owns the id registry. This protocol treats an id as opaque.
///     </para>
///     <para>
///         The fixed <see cref="RecordLength" /> is what makes an unrecognised id skippable, and
///         that in turn is what lets a new stat ship without a dialect version bump. An id is
///         permanent once shipped, exactly like an opcode.
///     </para>
/// </remarks>
/// <param name="StatId">The registry id this value belongs to.</param>
/// <param name="Value">The value.</param>
public readonly record struct ExtendedStat(ushort StatId, double Value)
{
    /// <summary>The exact size of one record on the wire.</summary>
    public const int RecordLength = 10;

    internal void Write(IPacketWriter writer)
    {
        writer.WriteUInt16(StatId);
        writer.WriteDouble(Value);
    }

    internal static ExtendedStat Read(ref PacketReader reader) =>
        new(reader.ReadUInt16(), reader.ReadDouble());
}
