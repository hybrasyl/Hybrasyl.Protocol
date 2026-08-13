using System;
using System.Collections.Generic;

namespace Hybrasyl.Protocol.Wire;

/// <summary>Decodes an extension-frame body into a typed extension packet.</summary>
internal delegate IExtensionPacket ExtensionDecodeFn(ReadOnlySpan<byte> body);

/// <summary>
///     One direction's <c>(dialect, opcode) -&gt; decoder</c> table, with latest-wins
///     resolution over each opcode's introduction dialects.
/// </summary>
internal sealed class ExtensionDispatchTable
{
    private readonly string _direction;

    // opcode -> entries, kept sorted by introduction dialect descending so Resolve returns the
    // first entry whose introduction is <= the requested dialect (the newest applicable shape).
    private readonly Dictionary<ushort, List<Entry>> _byOpcode = [];

    internal ExtensionDispatchTable(string direction) => _direction = direction;

    /// <summary>
    ///     This table's direction label (<c>"C-&gt;S"</c> / <c>"S-&gt;C"</c>), for messages about it.
    /// </summary>
    internal string Direction => _direction;

    /// <summary>The number of distinct opcodes with at least one registered shape.</summary>
    internal int OpcodeCount => _byOpcode.Count;

    /// <summary>The total number of registered <c>(opcode, introduction-dialect)</c> shapes.</summary>
    internal int ShapeCount
    {
        get
        {
            var total = 0;

            foreach (var entries in _byOpcode.Values)
                total += entries.Count;

            return total;
        }
    }

    /// <summary>
    ///     Registers a decoder for <paramref name="opcode" /> introduced at
    ///     <paramref name="introDialect" />. Throws on a duplicate
    ///     <c>(opcode, introDialect)</c> pair.
    /// </summary>
    internal void Add(ushort opcode, byte introDialect, ExtensionDecodeFn decode, Type packetType)
    {
        if (!_byOpcode.TryGetValue(opcode, out var entries))
        {
            entries = [];
            _byOpcode[opcode] = entries;
        }

        foreach (var existing in entries)
        {
            if (existing.IntroDialect == introDialect)
                throw new InvalidOperationException(
                    $"Duplicate {_direction} extension shape for opcode 0x{opcode:X4} at " +
                    $"dialect 0x{introDialect:X2}: {existing.PacketType.FullName} and " +
                    $"{packetType.FullName}.");
        }

        entries.Add(new Entry(introDialect, decode, packetType));
        entries.Sort(static (a, b) => b.IntroDialect.CompareTo(a.IntroDialect));
    }

    /// <summary>
    ///     Resolves the decoder for <paramref name="opcode" /> applicable at
    ///     <paramref name="dialect" /> - the registered shape with the highest introduction
    ///     dialect that is <c>&lt;= dialect</c>. Returns null if the opcode is unregistered or
    ///     every registered shape was introduced after <paramref name="dialect" />.
    /// </summary>
    internal ExtensionDecodeFn? Resolve(byte dialect, ushort opcode) =>
        ResolveEntry(dialect, opcode)?.Decode;

    /// <summary>
    ///     The concrete packet type <see cref="Resolve" /> would decode into at
    ///     <paramref name="dialect" />. This is what lets the <em>encode</em> side check that
    ///     the packet it was handed is the shape a peer will parse at the dialect being stamped -
    ///     the two must agree, or latest-wins resolution silently selects a different parser.
    /// </summary>
    internal Type? ResolveType(byte dialect, ushort opcode) =>
        ResolveEntry(dialect, opcode)?.PacketType;

    private Entry? ResolveEntry(byte dialect, ushort opcode)
    {
        if (!_byOpcode.TryGetValue(opcode, out var entries))
            return null;

        foreach (var entry in entries)
        {
            if (entry.IntroDialect <= dialect)
                return entry;
        }

        return null;
    }

    private readonly record struct Entry(byte IntroDialect, ExtensionDecodeFn Decode, Type PacketType);
}
