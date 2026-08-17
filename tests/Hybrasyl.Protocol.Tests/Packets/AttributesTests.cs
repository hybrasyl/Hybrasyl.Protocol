using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Packets;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Packets;

/// <summary>
///     The <c>0x0008</c> Attributes contract. The layout oracle here is hand-written rather than
///     produced by the writer under test, so a round-trip that agrees with itself under a wrong
///     byte order cannot pass it.
/// </summary>
public class AttributesTests
{
    // 0x01..0x2C, in order: level, ability, maxHp, maxMp, str, int, wis, con, dex, levelPoints,
    // maxWeight, currentWeight.
    private static readonly byte[] PrimaryBytes =
    [
        0x01, 0x02,
        0x03, 0x04,
        0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C,
        0x0D, 0x0E, 0x0F, 0x10,
        0x11, 0x12, 0x13, 0x14,
        0x15, 0x16, 0x17, 0x18,
        0x19, 0x1A, 0x1B, 0x1C,
        0x1D, 0x1E, 0x1F, 0x20,
        0x21, 0x22, 0x23, 0x24,
        0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x2B, 0x2C
    ];

    private static readonly byte[] VitalsBytes =
    [
        0x2D, 0x2E, 0x2F, 0x30,
        0x31, 0x32, 0x33, 0x34
    ];

    private static readonly byte[] ExperienceBytes =
    [
        0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x3B, 0x3C,
        0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44,
        0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C,
        0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54,
        0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x5B, 0x5C,
        0x5D, 0x5E, 0x5F, 0x60
    ];

    // ac, then IEEE 754 big-endian 1.5 / 2.0 / -0.5, then the six element bytes.
    private static readonly byte[] CombatBytes =
    [
        0x61, 0x62, 0x63, 0x64,
        0x3F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xBF, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x65, 0x66, 0x67, 0x68, 0x69, 0x6A
    ];

    private static readonly byte[] StatusBytes = [0x02, 0x01, 0x00, 0x01];

    // count = 2, then (0x1234, 1.0) and (0x00FF, -2.0).
    private static readonly byte[] ExtendedStatsBytes =
    [
        0x00, 0x02,
        0x12, 0x34, 0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0xFF, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private static Attributes FullyPopulated => new()
    {
        Primary = new AttributesPrimary(0x0102, 0x0304, 0x05060708, 0x090A0B0C, 0x0D0E0F10,
            0x11121314, 0x15161718, 0x191A1B1C, 0x1D1E1F20, 0x21222324, 0x25262728, 0x292A2B2C),
        Vitals = new AttributesVitals(0x2D2E2F30, 0x31323334),
        Experience = new AttributesExperience(0x35363738393A3B3C, 0x3D3E3F4041424344,
            0x45464748494A4B4C, 0x4D4E4F5051525354, 0x55565758595A5B5C, 0x5D5E5F60),
        Combat = new AttributesCombat(0x61626364, 1.5, 2.0, -0.5, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6A),
        Status = new AttributesStatus(2, Blinded: true, HasUnreadMail: false, HasParcel: true),
        ExtendedStats = [new ExtendedStat(0x1234, 1.0), new ExtendedStat(0x00FF, -2.0)]
    };

    [Fact]
    public void ToBody_WithEveryBlock_MatchesTheHandWrittenLayout()
    {
        byte[] expected =
        [
            0x00, 0x00, 0x00, 0x3F, // blockFlags: all six allocated bits
            .. PrimaryBytes,
            .. VitalsBytes,
            .. ExperienceBytes,
            .. CombatBytes,
            .. StatusBytes,
            .. ExtendedStatsBytes
        ];

        FullyPopulated.ToBody().Should().Equal(expected);
    }

    [Fact]
    public void ToBody_OmitsAbsentBlocksAndKeepsAscendingBitOrder()
    {
        // Vitals (0x02) and Status (0x10): the flag word names both, and Vitals precedes Status
        // even though Status is the block that was set first here.
        var packet = new Attributes
        {
            Status = new AttributesStatus(2, Blinded: true, HasUnreadMail: false, HasParcel: true),
            Vitals = new AttributesVitals(0x2D2E2F30, 0x31323334)
        };

        packet.ToBody().Should().Equal([0x00, 0x00, 0x00, 0x12, .. VitalsBytes, .. StatusBytes]);
    }

    [Fact]
    public void BlockFlags_AreDerivedFromWhichBlocksArePresent()
    {
        new Attributes().BlockFlags.Should().Be(AttributeBlockFlags.None);

        new Attributes { Vitals = new AttributesVitals(1, 2) }.BlockFlags
            .Should().Be(AttributeBlockFlags.Vitals);

        FullyPopulated.BlockFlags.Should().Be(
            AttributeBlockFlags.Primary | AttributeBlockFlags.Vitals
            | AttributeBlockFlags.Experience | AttributeBlockFlags.Combat
            | AttributeBlockFlags.Status | AttributeBlockFlags.ExtendedStats);
    }

    [Fact]
    public void EachBlock_EncodesToItsDeclaredLength()
    {
        // The declared sizes are exact - a receiver validates its body length against them - so
        // each is pinned against what the writer actually emits rather than left as prose.
        var full = FullyPopulated;

        Length(new Attributes { Primary = full.Primary })
            .Should().Be(AttributesPrimary.BlockLength);
        Length(new Attributes { Vitals = full.Vitals })
            .Should().Be(AttributesVitals.BlockLength);
        Length(new Attributes { Experience = full.Experience })
            .Should().Be(AttributesExperience.BlockLength);
        Length(new Attributes { Combat = full.Combat })
            .Should().Be(AttributesCombat.BlockLength);
        Length(new Attributes { Status = full.Status })
            .Should().Be(AttributesStatus.BlockLength);
        Length(new Attributes { ExtendedStats = full.ExtendedStats })
            .Should().Be(Attributes.ExtendedStatsCountLength + (2 * ExtendedStat.RecordLength));

        static int Length(Attributes packet) =>
            packet.ToBody().Length - Attributes.BlockFlagsLength;
    }

    [Fact]
    public void BodyLength_IsTheFlagWordPlusExactlyTheBlocksItSelects()
    {
        // The arithmetic a receiver rejects a body for failing.
        FullyPopulated.ToBody().Length.Should().Be(
            Attributes.BlockFlagsLength
            + AttributesPrimary.BlockLength
            + AttributesVitals.BlockLength
            + AttributesExperience.BlockLength
            + AttributesCombat.BlockLength
            + AttributesStatus.BlockLength
            + Attributes.ExtendedStatsCountLength + (2 * ExtendedStat.RecordLength));
    }

    [Fact]
    public void Parse_RoundTripsEveryBlock()
    {
        Attributes.Parse(FullyPopulated.ToBody()).Should().Be(FullyPopulated);
    }

    [Fact]
    public void Parse_LeavesAbsentBlocksNull()
    {
        var parsed = Attributes.Parse(new Attributes { Vitals = new AttributesVitals(7, 9) }
            .ToBody());

        parsed.Vitals.Should().Be(new AttributesVitals(7, 9));
        parsed.Primary.Should().BeNull();
        parsed.Experience.Should().BeNull();
        parsed.Combat.Should().BeNull();
        parsed.Status.Should().BeNull();
        parsed.ExtendedStats.Should().BeNull();
    }

    [Fact]
    public void Parse_OfAnEmptyBodyFlagWord_YieldsNoBlocks()
    {
        var parsed = Attributes.Parse([0x00, 0x00, 0x00, 0x00]);

        parsed.Should().Be(new Attributes());
        parsed.BlockFlags.Should().Be(AttributeBlockFlags.None);
    }

    [Theory]
    [InlineData(0x00000040u)] // the first reserved bit
    [InlineData(0x80000000u)] // the top bit
    [InlineData(0x00000042u)] // reserved alongside a block that is genuinely present
    public void Parse_RejectsAReservedBlockFlag(uint flags)
    {
        // A body long enough to satisfy the blocks it does name, so the rejection is the flag
        // check rather than a short read.
        byte[] body = [(byte)(flags >> 24), (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags,
            .. VitalsBytes];

        var act = () => Attributes.Parse(body);

        act.Should().Throw<InvalidDataException>().WithMessage("*reserved*");
    }

    [Fact]
    public void Parse_RejectsTrailingBytesAfterTheLastBlock()
    {
        byte[] body = [.. new Attributes { Vitals = new AttributesVitals(7, 9) }.ToBody(), 0xFF];

        var act = () => Attributes.Parse(body);

        act.Should().Throw<InvalidDataException>().WithMessage("*consume*");
    }

    [Fact]
    public void Parse_RejectsAnExtendedStatsCountThatOverrunsTheBody()
    {
        // Claims three records; only two follow.
        byte[] body = [0x00, 0x00, 0x00, 0x20, 0x00, 0x03, .. ExtendedStatsBytes[2..]];

        var act = () => Attributes.Parse(body);

        act.Should().Throw<InvalidDataException>().WithMessage("*declares 3 records*");
    }

    [Fact]
    public void Parse_RejectsAnExtendedStatsCountThatEndsShortOfTheBody()
    {
        // Claims one record; two follow, so the block does not end on the body boundary.
        byte[] body = [0x00, 0x00, 0x00, 0x20, 0x00, 0x01, .. ExtendedStatsBytes[2..]];

        var act = () => Attributes.Parse(body);

        act.Should().Throw<InvalidDataException>().WithMessage("*body boundary*");
    }

    [Fact]
    public void Parse_DistinguishesAnEmptySnapshotFromAnAbsentOne()
    {
        // The distinction is load-bearing: an empty snapshot clears the receiver's extended
        // state, where an absent block leaves it untouched.
        var empty = Attributes.Parse(new Attributes { ExtendedStats = [] }.ToBody());
        var absent = Attributes.Parse(new Attributes().ToBody());

        empty.ExtendedStats.Should().NotBeNull();
        empty.ExtendedStats!.Value.Should().BeEmpty();
        absent.ExtendedStats.Should().BeNull();
        empty.Should().NotBe(absent);
    }

    [Theory]
    [InlineData(0x04)] // the first reserved mode
    [InlineData(0x08)] // a value this dialect has never allocated, whatever else once did
    [InlineData(0xFF)]
    public void Parse_RejectsAReservedMovementMode(byte mode)
    {
        byte[] body = [0x00, 0x00, 0x00, 0x10, mode, 0x00, 0x00, 0x00];

        var act = () => Attributes.Parse(body);

        act.Should().Throw<InvalidDataException>().WithMessage("*movement mode*");
    }

    [Theory]
    [InlineData(0x04)]
    [InlineData(0xFF)]
    public void WriteBody_RefusesAReservedMovementMode(byte mode)
    {
        // Symmetric with the parse side: emitting one is our own defect, not peer input, so it
        // is refused before it reaches a wire where a conforming peer would drop the frame.
        var packet = new Attributes
        {
            Status = new AttributesStatus(mode, Blinded: false, HasUnreadMail: false,
                HasParcel: false)
        };

        var act = () => packet.ToBody();

        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(AttributesStatus.MaxMovementMode)]
    public void EveryAllocatedMovementMode_RoundTrips(byte mode)
    {
        // The positive half of the reservation, boundary included: without it, tightening the
        // check by one would reject mode 3 and no test would notice.
        var packet = new Attributes
        {
            Status = new AttributesStatus(mode, Blinded: true, HasUnreadMail: true,
                HasParcel: false)
        };

        Attributes.Parse(packet.ToBody()).Status!.MovementMode.Should().Be(mode);
    }

    [Fact]
    public void Parse_ReadsAnyNonZeroStatusByteAsTrue()
    {
        byte[] body = [0x00, 0x00, 0x00, 0x10, 0x03, 0xFF, 0x00, 0x02];

        Attributes.Parse(body).Status.Should()
            .Be(new AttributesStatus(3, Blinded: true, HasUnreadMail: false, HasParcel: true));
    }

    [Fact]
    public void Parse_RejectsABodyShorterThanTheBlocksItNames()
    {
        // Names Vitals but carries six of its eight bytes; DALib's reader throws, and the codec
        // is what normalises that to InvalidDataException.
        byte[] body = [0x00, 0x00, 0x00, 0x02, .. VitalsBytes[..6]];

        var act = () => Attributes.Parse(body);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(1.05)] // the 5% bonus retail's byte rating could express only approximately
    [InlineData(-0.5)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    [InlineData(1234.56789)]
    public void Combat_RoundTripsMultipliersExactly(double multiplier)
    {
        var packet = new Attributes
        {
            Combat = new AttributesCombat(-42, multiplier, multiplier, multiplier, 1, 2, 3, 4, 5,
                6)
        };

        var parsed = Attributes.Parse(packet.ToBody()).Combat!;

        parsed.Mr.Should().Be(multiplier);
        parsed.Dmg.Should().Be(multiplier);
        parsed.Hit.Should().Be(multiplier);
        parsed.Ac.Should().Be(-42);
    }

    [Fact]
    public void Experience_RoundTripsValuesAboveTheRetailCeiling()
    {
        // The point of the u64 widening: these are unrepresentable in retail's u32 fields.
        var packet = new Attributes
        {
            Experience = new AttributesExperience(ulong.MaxValue, 0x0000000100000000,
                0x1234567890ABCDEF, 0, 0xFFFFFFFFFFFFFFFF, int.MinValue)
        };

        Attributes.Parse(packet.ToBody()).Experience.Should().Be(packet.Experience);
    }

    [Fact]
    public void Attributes_RoundTripThroughTheCodec_ServerToClient()
    {
        var codec = new ExtensionCodec();

        codec.TryDecodeServer(codec.EncodeServer(FullyPopulated, Dialect.V1), out var decoded,
                out _, Dialect.V1)
            .Should().BeTrue();

        decoded.Should().Be(FullyPopulated);
    }

    [Fact]
    public void Attributes_IsRegisteredServerToClientOnly()
    {
        // S->C only, so a frame carrying 0x0008 in the client direction has nothing to resolve.
        var codec = new ExtensionCodec();
        var frame = codec.EncodeServer(FullyPopulated, Dialect.V1);

        var act = () => codec.TryDecodeClient(frame, out _, out _, Dialect.V1);

        act.Should().Throw<InvalidDataException>().WithMessage("*C->S*0x0008*");
    }

    [Fact]
    public void Opcode_IsTheRetailNumberZeroExtended()
    {
        ExtensionOpcodes.Attributes.Should().Be((ushort)0x0008);
        new Attributes().Opcode.Should().Be(ExtensionOpcodes.Attributes);
    }

    [Fact]
    public void Equality_ComparesExtendedStatsByValue()
    {
        // ImmutableArray compares by underlying-array reference, so without the override two
        // separately parsed copies of one packet would differ.
        var left = new Attributes { ExtendedStats = [new ExtendedStat(1, 2.0)] };
        var right = new Attributes { ExtendedStats = [new ExtendedStat(1, 2.0)] };
        var reordered = new Attributes
        {
            ExtendedStats = [new ExtendedStat(1, 2.0), new ExtendedStat(2, 1.0)]
        };

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.Should().NotBe(reordered);
        left.Should().NotBe(new Attributes { ExtendedStats = [new ExtendedStat(1, 2.5)] });
    }

    [Fact]
    public void ExtendedStats_SetToAnUninitialisedArray_ReadsAsAnEmptySnapshot()
    {
        var packet = new Attributes { ExtendedStats = default(ImmutableArray<ExtendedStat>) };

        packet.ExtendedStats!.Value.Should().BeEmpty();
        packet.ToBody().Should().Equal(0x00, 0x00, 0x00, 0x20, 0x00, 0x00);
    }

    [Fact]
    public void WriteBody_RefusesMoreExtendedStatsThanTheCountFieldCanExpress()
    {
        var act = () => Snapshot(ushort.MaxValue + 1).ToBody();

        act.Should().Throw<InvalidOperationException>().WithMessage("*u16*");
    }

    [Fact]
    public void WriteBody_AcceptsExactlyAsManyExtendedStatsAsTheCountFieldHolds()
    {
        // The positive half of the cap: without it, tightening the guard by one would reject a
        // legal snapshot and no test would notice.
        var body = Snapshot(ushort.MaxValue).ToBody();

        body.Length.Should().Be(Attributes.BlockFlagsLength + Attributes.ExtendedStatsCountLength
            + (ushort.MaxValue * ExtendedStat.RecordLength));
        body.AsSpan(4, 2).ToArray().Should().Equal(0xFF, 0xFF);
    }

    private static Attributes Snapshot(int count) => new()
    {
        ExtendedStats = [.. Enumerable.Range(0, count).Select(i => new ExtendedStat((ushort)i, i))]
    };
}
