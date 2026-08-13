using System;
using System.Buffers.Binary;
using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Body-serialization primitives the extension protocol needs and DALib does not provide.
///     Retail never carried a value wider than 32 bits, so DALib's reader and writer stop there;
///     extension packets are not bound by what retail happened to need.
/// </summary>
/// <remarks>
///     These extend DALib's <em>public</em> surface rather than widening DALib itself, which
///     models retail as ground truth. Byte order is big-endian, matching every other multi-byte
///     field on this wire.
/// </remarks>
public static class ExtensionWire
{
    /// <summary>Writes a big-endian <c>u64</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="writer" /> is null.</exception>
    public static void WriteUInt64(this IPacketWriter writer, ulong value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        writer.WriteBytes(bytes);
    }

    /// <summary>Writes a big-endian <c>i64</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="writer" /> is null.</exception>
    public static void WriteInt64(this IPacketWriter writer, long value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        writer.WriteBytes(bytes);
    }

    /// <summary>Reads a big-endian <c>u64</c>.</summary>
    /// <exception cref="InvalidOperationException">Fewer than eight bytes remain.</exception>
    public static ulong ReadUInt64(this ref PacketReader reader) =>
        BinaryPrimitives.ReadUInt64BigEndian(reader.ReadBytes(sizeof(ulong)));

    /// <summary>Reads a big-endian <c>i64</c>.</summary>
    /// <exception cref="InvalidOperationException">Fewer than eight bytes remain.</exception>
    public static long ReadInt64(this ref PacketReader reader) =>
        BinaryPrimitives.ReadInt64BigEndian(reader.ReadBytes(sizeof(long)));
}
