using System;
using System.Collections.Generic;

namespace Hybrasyl.Protocol.Wire;

/// <summary>Decodes an extension-frame body into a typed extension packet.</summary>
internal delegate IExtensionPacket ExtensionDecodeFn(ReadOnlySpan<byte> body);

/// <summary>
///     One direction's <c>(signature, opcode) -&gt; decoder</c> table, with latest-wins
///     resolution over each opcode's introduction signatures.
/// </summary>
internal sealed class ExtensionDispatchTable
{
    private readonly string _direction;

    // opcode -> entries, kept sorted by introduction signature descending so Resolve returns the
    // first entry whose introduction is <= the requested signature (the newest applicable shape).
    private readonly Dictionary<ushort, List<Entry>> _byOpcode = [];

    internal ExtensionDispatchTable(string direction) => _direction = direction;

    /// <summary>The number of distinct opcodes with at least one registered shape.</summary>
    internal int OpcodeCount => _byOpcode.Count;

    /// <summary>The total number of registered <c>(opcode, introduction-signature)</c> shapes.</summary>
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
    ///     <paramref name="introSignature" />. Throws on a duplicate
    ///     <c>(opcode, introSignature)</c> pair.
    /// </summary>
    internal void Add(ushort opcode, byte introSignature, ExtensionDecodeFn decode, string typeName)
    {
        if (!_byOpcode.TryGetValue(opcode, out var entries))
        {
            entries = [];
            _byOpcode[opcode] = entries;
        }

        foreach (var existing in entries)
        {
            if (existing.IntroSignature == introSignature)
                throw new InvalidOperationException(
                    $"Duplicate {_direction} extension shape for opcode 0x{opcode:X4} at " +
                    $"signature 0x{introSignature:X2}: {existing.TypeName} and {typeName}.");
        }

        entries.Add(new Entry(introSignature, decode, typeName));
        // Keep descending by introduction signature so Resolve scans newest-first.
        entries.Sort(static (a, b) => b.IntroSignature.CompareTo(a.IntroSignature));
    }

    /// <summary>
    ///     Resolves the decoder for <paramref name="opcode" /> applicable at
    ///     <paramref name="signature" /> - the registered shape with the highest introduction
    ///     signature that is <c>&lt;= signature</c>. Returns null if the opcode is unregistered or
    ///     every registered shape was introduced after <paramref name="signature" />.
    /// </summary>
    internal ExtensionDecodeFn? Resolve(byte signature, ushort opcode)
    {
        if (!_byOpcode.TryGetValue(opcode, out var entries))
            return null;

        foreach (var entry in entries)
        {
            if (entry.IntroSignature <= signature)
                return entry.Decode;
        }

        return null;
    }

    private readonly record struct Entry(byte IntroSignature, ExtensionDecodeFn Decode, string TypeName);
}
