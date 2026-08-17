using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The <see cref="AttributeBlockFlags.Combat" /> block of an <see cref="Attributes" /> body:
///     armour class, the three combat multipliers, and the offensive and defensive elements.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Mr" />, <see cref="Dmg" /> and <see cref="Hit" /> are multipliers centred on
///         <c>1.0</c> - <c>1.05</c> is a 5% bonus - sent raw rather than as retail's byte rating,
///         whose quantization capped the expressible range at roughly &#177;0.16 regardless of
///         what the server computed.
///     </para>
///     <para>
///         Each element carries three values. <see cref="OffensiveElement" /> is the effective
///         value the receiver displays; the sender has already resolved
///         <c>override == none ? base : override</c>, which is a <em>game rule rather than a
///         formatting transform</em>, so a receiver must not re-derive it. Base and override ride
///         along for breakdown display only.
///     </para>
/// </remarks>
/// <param name="Ac">Armour class. Signed, and lower is better, per retail convention.</param>
/// <param name="Mr">Magic resistance multiplier.</param>
/// <param name="Dmg">Damage multiplier.</param>
/// <param name="Hit">Hit multiplier.</param>
/// <param name="OffensiveElement">Effective offensive element, resolved by the sender.</param>
/// <param name="OffensiveElementBase">Intrinsic offensive element.</param>
/// <param name="OffensiveElementOverride">Temporary offensive element, <c>0</c> when none.</param>
/// <param name="DefensiveElement">Effective defensive element, resolved by the sender.</param>
/// <param name="DefensiveElementBase">Intrinsic defensive element.</param>
/// <param name="DefensiveElementOverride">Temporary defensive element, <c>0</c> when none.</param>
public sealed record AttributesCombat(
    int Ac,
    double Mr,
    double Dmg,
    double Hit,
    byte OffensiveElement,
    byte OffensiveElementBase,
    byte OffensiveElementOverride,
    byte DefensiveElement,
    byte DefensiveElementBase,
    byte DefensiveElementOverride)
{
    /// <summary>The exact size of this block on the wire.</summary>
    public const int BlockLength = 34;

    internal void Write(IPacketWriter writer)
    {
        writer.WriteInt32(Ac);
        writer.WriteDouble(Mr);
        writer.WriteDouble(Dmg);
        writer.WriteDouble(Hit);
        writer.WriteByte(OffensiveElement);
        writer.WriteByte(OffensiveElementBase);
        writer.WriteByte(OffensiveElementOverride);
        writer.WriteByte(DefensiveElement);
        writer.WriteByte(DefensiveElementBase);
        writer.WriteByte(DefensiveElementOverride);
    }

    internal static AttributesCombat Read(ref PacketReader reader) =>
        new(reader.ReadInt32(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadDouble(),
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadByte());
}
