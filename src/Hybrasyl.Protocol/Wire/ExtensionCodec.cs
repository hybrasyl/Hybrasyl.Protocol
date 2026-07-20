using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Encodes and decodes native extended-framing packets: the typed layer above
///     <see cref="ExtensionFrameCodec" />. It handles only <em>declared</em> extension packets -
///     new packets (<c>0x0100+</c>) and explicit replacements of a retail opcode with an upgraded
///     shape. Un-migrated retail packets are not its concern; they travel as literal <c>0xAA</c>
///     frames on DALib's codec (see <see cref="FrameRouter" />).
/// </summary>
/// <remarks>
///     The codec carries no crypto - extension frames ride inside TLS 1.3, which owns
///     confidentiality, integrity, and replay protection. It is stateless and safe to share
///     process-wide. Resolution is latest-wins over each opcode's introduction signatures.
/// </remarks>
public sealed class ExtensionCodec
{
    private readonly ExtensionDispatchTable _clientTable;
    private readonly ExtensionDispatchTable _serverTable;

    /// <summary>Constructs a codec that discovers extension packets in sreang only.</summary>
    public ExtensionCodec()
        : this([]) { }

    /// <summary>
    ///     Constructs a codec that discovers extension packets in sreang plus the supplied
    ///     <paramref name="packetAssemblies" /> (where a consumer declares its extension packets).
    /// </summary>
    public ExtensionCodec(IEnumerable<Assembly> packetAssemblies)
    {
        ArgumentNullException.ThrowIfNull(packetAssemblies);

        var assemblies = new HashSet<Assembly> { typeof(IExtensionPacket).Assembly };

        foreach (var assembly in packetAssemblies)
        {
            if (assembly is not null)
                assemblies.Add(assembly);
        }

        (_clientTable, _serverTable) = ExtensionDispatchBuilder.Build(assemblies);
    }

    /// <summary>The number of distinct C-&gt;S extension opcodes the codec can decode.</summary>
    public int RegisteredClientOpcodeCount => _clientTable.OpcodeCount;

    /// <summary>The number of distinct S-&gt;C extension opcodes the codec can decode.</summary>
    public int RegisteredServerOpcodeCount => _serverTable.OpcodeCount;

    /// <summary>Encodes a C-&gt;S extension packet into a frame stamped with
    ///     <paramref name="dialect" />.</summary>
    public byte[] EncodeClient(
        IExtensionClientPacket packet,
        Dialect dialect,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return ExtensionFrameCodec.WriteFrame(dialect, packet.Opcode, packet.ToBody(), flags,
            maxFrameSize);
    }

    /// <summary>Encodes an S-&gt;C extension packet into a frame stamped with
    ///     <paramref name="dialect" />.</summary>
    public byte[] EncodeServer(
        IExtensionServerPacket packet,
        Dialect dialect,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return ExtensionFrameCodec.WriteFrame(dialect, packet.Opcode, packet.ToBody(), flags,
            maxFrameSize);
    }

    /// <summary>
    ///     Attempts to decode a single C-&gt;S extension frame from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if a complete frame was decoded; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The frame is malformed, or no extension packet is
    ///     registered for its <c>(signature, opcode)</c>.</exception>
    public bool TryDecodeClient(
        ReadOnlyMemory<byte> buffer,
        out IExtensionClientPacket? packet,
        out int bytesConsumed,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        var ok = TryDecode(_clientTable, "C->S", buffer, out var decoded, out bytesConsumed,
            maxFrameSize);
        packet = (IExtensionClientPacket?)decoded;

        return ok;
    }

    /// <summary>
    ///     Attempts to decode a single S-&gt;C extension frame from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    public bool TryDecodeServer(
        ReadOnlyMemory<byte> buffer,
        out IExtensionServerPacket? packet,
        out int bytesConsumed,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        var ok = TryDecode(_serverTable, "S->C", buffer, out var decoded, out bytesConsumed,
            maxFrameSize);
        packet = (IExtensionServerPacket?)decoded;

        return ok;
    }

    private static bool TryDecode(
        ExtensionDispatchTable table,
        string direction,
        ReadOnlyMemory<byte> buffer,
        out IExtensionPacket? packet,
        out int bytesConsumed,
        int maxFrameSize)
    {
        packet = null;
        bytesConsumed = 0;

        if (!ExtensionFrameCodec.TryReadFrame(buffer, out var header, out var body,
                out var consumed, maxFrameSize))
            return false;

        var decode = table.Resolve(header.Signature, header.Opcode)
            ?? throw new InvalidDataException(
                $"No registered {direction} extension packet for opcode 0x{header.Opcode:X4} " +
                $"at signature 0x{header.Signature:X2}.");

        packet = decode(body.Span);
        bytesConsumed = consumed;

        return true;
    }
}
