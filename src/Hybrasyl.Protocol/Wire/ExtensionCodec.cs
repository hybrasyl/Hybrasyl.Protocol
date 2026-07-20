using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Encodes and decodes extended-framing packets: the typed layer above
///     <see cref="ExtensionFrameCodec" />. It composes DALib's retail packet types (available at
///     signature <c>0xAA</c>, zero-extended into the <c>u16</c> opcode space) with native
///     extension packets, and resolves each incoming <c>(signature, opcode)</c> to the newest
///     applicable shape.
/// </summary>
/// <remarks>
///     The codec carries no crypto - extension frames ride inside TLS 1.3, which owns
///     confidentiality, integrity, and replay protection. It is stateless and safe to share
///     process-wide.
/// </remarks>
public sealed class ExtensionCodec
{
    private readonly ExtensionDispatchTable _clientTable;
    private readonly ExtensionDispatchTable _serverTable;

    /// <summary>
    ///     Constructs a codec composing DALib's retail packets and sreang's own types only.
    /// </summary>
    public ExtensionCodec()
        : this([]) { }

    /// <summary>
    ///     Constructs a codec composing DALib's retail packets, sreang's own types, and the
    ///     supplied <paramref name="extraPacketAssemblies" /> (where a consumer declares its
    ///     native extension packets). DALib and sreang are always included - retail composition
    ///     is the whole point - so callers pass only their own extra assemblies.
    /// </summary>
    public ExtensionCodec(IEnumerable<Assembly> extraPacketAssemblies)
    {
        ArgumentNullException.ThrowIfNull(extraPacketAssemblies);

        var assemblies = new HashSet<Assembly>
        {
            typeof(IPacket).Assembly,           // DALib retail packets
            typeof(IExtensionPacket).Assembly,  // sreang
        };

        foreach (var assembly in extraPacketAssemblies)
        {
            if (assembly is not null)
                assemblies.Add(assembly);
        }

        (_clientTable, _serverTable) = ExtensionDispatchBuilder.Build(assemblies);
    }

    /// <summary>The number of distinct C-&gt;S opcodes the codec can decode.</summary>
    public int RegisteredClientOpcodeCount => _clientTable.OpcodeCount;

    /// <summary>The number of distinct S-&gt;C opcodes the codec can decode.</summary>
    public int RegisteredServerOpcodeCount => _serverTable.OpcodeCount;

    /// <summary>Encodes a native C-&gt;S extension packet into a frame stamped with
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

    /// <summary>Encodes a native S-&gt;C extension packet into a frame stamped with
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
    ///     Encodes a retail-mirrored DALib C-&gt;S packet into an extension frame - its retail
    ///     opcode zero-extended, its body written verbatim. Used when a connection speaks a
    ///     dialect but a given packet is unchanged from retail.
    /// </summary>
    public byte[] EncodeRetailClient(
        IClientPacket packet,
        Dialect dialect,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return ExtensionFrameCodec.WriteFrame(dialect, packet.Opcode, packet.ToBody(), flags,
            maxFrameSize);
    }

    /// <summary>
    ///     Encodes a retail-mirrored DALib S-&gt;C packet into an extension frame - its retail
    ///     opcode zero-extended, its body written verbatim.
    /// </summary>
    public byte[] EncodeRetailServer(
        IServerPacket packet,
        Dialect dialect,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return ExtensionFrameCodec.WriteFrame(dialect, packet.Opcode, packet.ToBody(), flags,
            maxFrameSize);
    }

    /// <summary>
    ///     Attempts to decode a single C-&gt;S frame from the front of <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if a complete frame was decoded; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The frame is malformed, or no packet is registered
    ///     for its <c>(signature, opcode)</c>.</exception>
    public bool TryDecodeClient(
        ReadOnlyMemory<byte> buffer,
        out DecodedPacket packet,
        out int bytesConsumed,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize) =>
        TryDecode(_clientTable, "C->S", buffer, out packet, out bytesConsumed, maxFrameSize);

    /// <summary>
    ///     Attempts to decode a single S-&gt;C frame from the front of <paramref name="buffer" />.
    /// </summary>
    public bool TryDecodeServer(
        ReadOnlyMemory<byte> buffer,
        out DecodedPacket packet,
        out int bytesConsumed,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize) =>
        TryDecode(_serverTable, "S->C", buffer, out packet, out bytesConsumed, maxFrameSize);

    private static bool TryDecode(
        ExtensionDispatchTable table,
        string direction,
        ReadOnlyMemory<byte> buffer,
        out DecodedPacket packet,
        out int bytesConsumed,
        int maxFrameSize)
    {
        packet = default;
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
