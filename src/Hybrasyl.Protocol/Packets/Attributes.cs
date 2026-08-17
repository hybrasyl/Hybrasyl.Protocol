using System;
using System.Collections.Immutable;
using System.IO;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The player's stats, vitals, experience and combat values:
///     <c>[u32 blockFlags][blocks...]</c>, S-&gt;C. Replaces retail <c>0x08</c> for the duration
///     of a negotiated connection.
/// </summary>
/// <remarks>
///     <para>
///         Every block is optional and <see cref="BlockFlags" /> is derived from which are
///         present, so the flag word and the blocks cannot disagree.
///     </para>
///     <para>
///         <b>Blocks are state, not deltas.</b> A present block replaces whatever the receiver
///         held for it; an absent block leaves it unchanged. A present
///         <see cref="ExtendedStats" /> is a <em>complete</em> snapshot and replaces extended
///         state wholesale, dropping ids that are no longer there - absence of an id inside a
///         present block means the value no longer applies, not that it is unchanged. That
///         asymmetry is forced: partial sends are safe for the fixed blocks only because that set
///         is closed, and the extended id space is open by design.
///     </para>
///     <para>
///         A server should keep emitting retail <c>0x08</c> alongside this on negotiated
///         connections - not for compatibility, since a dialect client can ignore it, but so that
///         path stays exercised rather than running only once the dialect has already failed. A
///         receiver with an engaged dialect selects this packet as its source for every field the
///         two share; that is source selection, not a merge.
///     </para>
/// </remarks>
[ExtensionServerOpcode(ExtensionOpcodes.Attributes, Dialect.V1)]
public sealed record Attributes : ExtensionServerPacket
{
    /// <summary>The size of the leading <c>blockFlags</c> word.</summary>
    public const int BlockFlagsLength = 4;

    /// <summary>The size of the <c>u16</c> record count leading an extended-stat snapshot.</summary>
    public const int ExtendedStatsCountLength = 2;

    private const AttributeBlockFlags AllocatedBlocks =
        AttributeBlockFlags.Primary
        | AttributeBlockFlags.Vitals
        | AttributeBlockFlags.Experience
        | AttributeBlockFlags.Combat
        | AttributeBlockFlags.Status
        | AttributeBlockFlags.ExtendedStats;

    private readonly ImmutableArray<ExtendedStat>? _extendedStats;

    /// <inheritdoc />
    public override ushort Opcode => ExtensionOpcodes.Attributes;

    /// <summary>Level, primary stats and carry weight, or <see langword="null" /> when absent.</summary>
    public AttributesPrimary? Primary { get; init; }

    /// <summary>Current hit and magic points, or <see langword="null" /> when absent.</summary>
    public AttributesVitals? Vitals { get; init; }

    /// <summary>Experience, gold and game points, or <see langword="null" /> when absent.</summary>
    public AttributesExperience? Experience { get; init; }

    /// <summary>Armour class, combat multipliers and elements, or <see langword="null" /> when
    ///     absent.</summary>
    public AttributesCombat? Combat { get; init; }

    /// <summary>Movement mode and the standalone status booleans, or <see langword="null" /> when
    ///     absent.</summary>
    public AttributesStatus? Status { get; init; }

    /// <summary>
    ///     A complete snapshot of extended stats, or <see langword="null" /> when absent. An empty
    ///     snapshot is not the same as an absent one: it clears the receiver's extended state,
    ///     where absence leaves it alone.
    /// </summary>
    public ImmutableArray<ExtendedStat>? ExtendedStats
    {
        get => _extendedStats;

        // An uninitialised ImmutableArray throws on every use, so fold it into the empty snapshot
        // it already reads like rather than faulting at write time.
        init => _extendedStats = value is { IsDefault: true } ? [] : value;
    }

    /// <summary>
    ///     The <c>blockFlags</c> word this packet writes, derived from which blocks are present.
    /// </summary>
    public AttributeBlockFlags BlockFlags =>
        (Primary is null ? 0 : AttributeBlockFlags.Primary)
        | (Vitals is null ? 0 : AttributeBlockFlags.Vitals)
        | (Experience is null ? 0 : AttributeBlockFlags.Experience)
        | (Combat is null ? 0 : AttributeBlockFlags.Combat)
        | (Status is null ? 0 : AttributeBlockFlags.Status)
        | (ExtendedStats is null ? 0 : AttributeBlockFlags.ExtendedStats);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException"><see cref="ExtendedStats" /> holds more
    ///     records than the <c>u16</c> count field can express.</exception>
    public override void WriteBody(IPacketWriter writer)
    {
        writer.WriteUInt32((uint)BlockFlags);

        Primary?.Write(writer);
        Vitals?.Write(writer);
        Experience?.Write(writer);
        Combat?.Write(writer);
        Status?.Write(writer);

        if (ExtendedStats is not { } stats)
            return;

        if (stats.Length > ushort.MaxValue)
            throw new InvalidOperationException(
                $"Attributes carries {stats.Length} extended stats; the count field is a u16 and "
                + $"caps the block at {ushort.MaxValue}.");

        writer.WriteUInt16((ushort)stats.Length);

        foreach (var stat in stats)
            stat.Write(writer);
    }

    /// <summary>Parses an <see cref="Attributes" /> body.</summary>
    /// <exception cref="InvalidDataException"><paramref name="body" /> sets a reserved block flag,
    ///     declares an extended-stat count that does not end on the body boundary, or does not
    ///     match the exact size of the blocks it selects.</exception>
    public static Attributes Parse(ReadOnlySpan<byte> body)
    {
        var reader = new PacketReader(body);
        var flags = (AttributeBlockFlags)reader.ReadUInt32();
        var reserved = flags & ~AllocatedBlocks;

        if (reserved != 0)
            throw new InvalidDataException(
                $"Attributes blockFlags 0x{(uint)flags:X8} sets reserved bit(s) "
                + $"0x{(uint)reserved:X8}. A fixed-size block cannot be skipped without knowing "
                + "its size, so an unrecognised block flag is a protocol error rather than "
                + "something a receiver can step over.");

        // Ascending bit order, which a sender must not vary.
        var primary = Has(flags, AttributeBlockFlags.Primary)
            ? AttributesPrimary.Read(ref reader)
            : null;
        var vitals = Has(flags, AttributeBlockFlags.Vitals)
            ? AttributesVitals.Read(ref reader)
            : null;
        var experience = Has(flags, AttributeBlockFlags.Experience)
            ? AttributesExperience.Read(ref reader)
            : null;
        var combat = Has(flags, AttributeBlockFlags.Combat)
            ? AttributesCombat.Read(ref reader)
            : null;
        var status = Has(flags, AttributeBlockFlags.Status)
            ? AttributesStatus.Read(ref reader)
            : null;
        ImmutableArray<ExtendedStat>? extendedStats = Has(flags, AttributeBlockFlags.ExtendedStats)
            ? ReadExtendedStats(ref reader)
            : null;

        if (reader.Position != body.Length)
            throw new InvalidDataException(
                $"Attributes body is {body.Length} bytes; the blocks selected by "
                + $"0x{(uint)flags:X8} consume {reader.Position}.");

        return new Attributes
        {
            Primary = primary,
            Vitals = vitals,
            Experience = experience,
            Combat = combat,
            Status = status,
            ExtendedStats = extendedStats
        };
    }

    /// <inheritdoc />
    public bool Equals(Attributes? other) =>
        other is not null
        && Primary == other.Primary
        && Vitals == other.Vitals
        && Experience == other.Experience
        && Combat == other.Combat
        && Status == other.Status
        && ExtendedStatsEqual(ExtendedStats, other.ExtendedStats);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Primary);
        hash.Add(Vitals);
        hash.Add(Experience);
        hash.Add(Combat);
        hash.Add(Status);

        if (ExtendedStats is { } stats)
        {
            hash.Add(stats.Length);

            foreach (var stat in stats)
                hash.Add(stat);
        }

        return hash.ToHashCode();
    }

    private static bool Has(AttributeBlockFlags flags, AttributeBlockFlags block) =>
        (flags & block) != 0;

    /// <summary>
    ///     Sequence equality, which the compiler-generated record equality would not give:
    ///     <see cref="ImmutableArray{T}" /> compares by underlying-array reference, so two
    ///     separately parsed copies of one packet would not be equal.
    /// </summary>
    private static bool ExtendedStatsEqual(
        ImmutableArray<ExtendedStat>? left,
        ImmutableArray<ExtendedStat>? right)
    {
        if (left is not { } l || right is not { } r)
            return left is null && right is null;

        return l.AsSpan().SequenceEqual(r.AsSpan());
    }

    private static ImmutableArray<ExtendedStat> ReadExtendedStats(ref PacketReader reader)
    {
        var count = reader.ReadUInt16();
        var declared = count * ExtendedStat.RecordLength;

        // Validated against the bytes actually present before allocating, rather than trusting a
        // peer-supplied count; this also enforces the block ending on the body boundary.
        if (reader.Remaining.Length != declared)
            throw new InvalidDataException(
                $"Attributes ExtendedStats declares {count} records ({declared} bytes) but "
                + $"{reader.Remaining.Length} bytes remain; the block must consume exactly "
                + $"{ExtendedStat.RecordLength} bytes per record and end on the body boundary.");

        var stats = ImmutableArray.CreateBuilder<ExtendedStat>(count);

        for (var i = 0; i < count; i++)
            stats.Add(ExtendedStat.Read(ref reader));

        return stats.MoveToImmutable();
    }
}
