using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The <see cref="AttributeBlockFlags.Primary" /> block of an <see cref="Attributes" /> body:
///     level, ability, the five primary stats, and carry weight.
/// </summary>
/// <remarks>
///     Retail carried the stats as <c>u8</c> and the weights as <c>u16</c>, so a configured stat
///     cap above 255 wrapped silently. The widths here are <c>u32</c> so that a cap is a balance
///     decision rather than a wire artifact.
/// </remarks>
/// <param name="Level">Character level.</param>
/// <param name="Ability">Ability (master) level.</param>
/// <param name="MaxHp">Maximum hit points.</param>
/// <param name="MaxMp">Maximum magic points.</param>
/// <param name="Str">Effective strength.</param>
/// <param name="Int">Effective intelligence.</param>
/// <param name="Wis">Effective wisdom.</param>
/// <param name="Con">Effective constitution.</param>
/// <param name="Dex">Effective dexterity.</param>
/// <param name="LevelPoints">Unspent stat points. Retail carried a separate "has level points"
///     boolean alongside this; <c>LevelPoints &gt; 0</c> states the same fact.</param>
/// <param name="MaxWeight">Maximum carry weight.</param>
/// <param name="CurrentWeight">Weight currently carried.</param>
public sealed record AttributesPrimary(
    ushort Level,
    ushort Ability,
    uint MaxHp,
    uint MaxMp,
    uint Str,
    uint Int,
    uint Wis,
    uint Con,
    uint Dex,
    uint LevelPoints,
    uint MaxWeight,
    uint CurrentWeight)
{
    /// <summary>The exact size of this block on the wire.</summary>
    public const int BlockLength = 44;

    internal void Write(IPacketWriter writer)
    {
        writer.WriteUInt16(Level);
        writer.WriteUInt16(Ability);
        writer.WriteUInt32(MaxHp);
        writer.WriteUInt32(MaxMp);
        writer.WriteUInt32(Str);
        writer.WriteUInt32(Int);
        writer.WriteUInt32(Wis);
        writer.WriteUInt32(Con);
        writer.WriteUInt32(Dex);
        writer.WriteUInt32(LevelPoints);
        writer.WriteUInt32(MaxWeight);
        writer.WriteUInt32(CurrentWeight);
    }

    internal static AttributesPrimary Read(ref PacketReader reader) =>
        new(reader.ReadUInt16(),
            reader.ReadUInt16(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32());
}
