using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The <see cref="AttributeBlockFlags.Experience" /> block of an <see cref="Attributes" />
///     body: experience, ability experience, gold and game points.
/// </summary>
/// <remarks>
///     The <c>u64</c> widths remove retail's 4.29-billion ceiling on experience and gold - the
///     same problem as the primary stat bytes, further out.
/// </remarks>
/// <param name="Experience">Total experience earned.</param>
/// <param name="ExpToLevel">Experience still required for the next level.</param>
/// <param name="AbilityExp">Total ability experience earned.</param>
/// <param name="AbilityToNext">Ability experience still required for the next ability level.</param>
/// <param name="Gold">Gold carried.</param>
/// <param name="GamePoints">Game points held.</param>
public sealed record AttributesExperience(
    ulong Experience,
    ulong ExpToLevel,
    ulong AbilityExp,
    ulong AbilityToNext,
    ulong Gold,
    int GamePoints)
{
    /// <summary>The exact size of this block on the wire.</summary>
    public const int BlockLength = 44;

    internal void Write(IPacketWriter writer)
    {
        writer.WriteUInt64(Experience);
        writer.WriteUInt64(ExpToLevel);
        writer.WriteUInt64(AbilityExp);
        writer.WriteUInt64(AbilityToNext);
        writer.WriteUInt64(Gold);
        writer.WriteInt32(GamePoints);
    }

    internal static AttributesExperience Read(ref PacketReader reader) =>
        new(reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadInt32());
}
