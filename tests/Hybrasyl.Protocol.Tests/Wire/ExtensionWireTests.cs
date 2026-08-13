using System;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>
///     The 64-bit body primitives. Byte order is the only thing that matters here and it is not
///     checkable from the call site, so every case pins literal bytes rather than round-tripping
///     through the same code that wrote them.
/// </summary>
public class ExtensionWireTests
{
    private static byte[] Written(Action<IPacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);

        return writer.ToArray();
    }

    [Fact]
    public void WriteUInt64_IsBigEndian()
    {
        Written(w => w.WriteUInt64(0x0123456789ABCDEF))
            .Should().Equal(0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF);
    }

    [Fact]
    public void WriteInt64_IsBigEndian_IncludingNegatives()
    {
        Written(w => w.WriteInt64(0x0123456789ABCDEF))
            .Should().Equal(0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF);

        // -2 is two's complement 0xFFFF_FFFF_FFFF_FFFE; the sign lives in the first byte written,
        // which is what makes this a big-endian assertion rather than a value one.
        Written(w => w.WriteInt64(-2))
            .Should().Equal(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0x00000000FFFFFFFFUL)] // low half only - catches swapped halves
    [InlineData(0xFFFFFFFF00000000UL)] // high half only
    [InlineData(0x0123456789ABCDEFUL)]
    public void UInt64_RoundTrips(ulong value)
    {
        var reader = new PacketReader(Written(w => w.WriteUInt64(value)));

        reader.ReadUInt64().Should().Be(value);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(-0x0123456789ABCDEFL)]
    public void Int64_RoundTrips(long value)
    {
        var reader = new PacketReader(Written(w => w.WriteInt64(value)));

        reader.ReadInt64().Should().Be(value);
    }

    [Fact]
    public void Read_PastTheEnd_Throws()
    {
        var act = () =>
        {
            var reader = new PacketReader(new byte[7]);

            return reader.ReadUInt64();
        };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Write_NullWriter_Throws()
    {
        var unsigned = () => ((IPacketWriter)null!).WriteUInt64(0);
        var signed = () => ((IPacketWriter)null!).WriteInt64(0);

        unsigned.Should().Throw<ArgumentNullException>();
        signed.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SixtyFourBitValues_AreContiguousWithNeighbouringFields()
    {
        // Guards against a primitive that writes correct bytes but leaves the writer's position
        // wrong - invisible in a single-field body, corrupting in a real one.
        var bytes = Written(w =>
        {
            w.WriteByte(0xAA);
            w.WriteUInt64(0x0011223344556677);
            w.WriteUInt16(0xBBCC);
        });

        bytes.Should().Equal(0xAA, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0xBB, 0xCC);

        var reader = new PacketReader(bytes);
        reader.ReadByte().Should().Be(0xAA);
        reader.ReadUInt64().Should().Be(0x0011223344556677UL);
        reader.ReadUInt16().Should().Be((ushort)0xBBCC);
    }
}
