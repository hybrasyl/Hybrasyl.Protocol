using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Framing;
using Hybrasyl.Protocol.Negotiation;

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
///     process-wide. Resolution is latest-wins over each opcode's introduction dialects.
/// </remarks>
public sealed class ExtensionCodec
{
    private readonly ExtensionDispatchTable _clientTable;
    private readonly ExtensionDispatchTable _serverTable;

    /// <summary>Constructs a codec that discovers extension packets in sreang only.</summary>
    public ExtensionCodec()
        : this([]) { }

    /// <summary>
    ///     Constructs a codec that discovers extension packets in this library plus the supplied
    ///     <paramref name="packetAssemblies" />.
    /// </summary>
    /// <remarks>
    ///     <strong>For consumer-private extensions only.</strong> A shape <em>both ends</em> speak
    ///     must be declared in this library: a type declared in one consumer's assembly leaves the
    ///     other consumer with nothing to resolve, and the failure is a decode error on a live
    ///     connection rather than anything a build catches.
    /// </remarks>
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

    /// <summary>
    ///     Encodes a C-&gt;S extension packet into a frame stamped with
    ///     <paramref name="dialect" />, after checking that <paramref name="packet" /> is the
    ///     shape a peer resolves at that dialect.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="packet" /> is not the shape <paramref name="dialect" /> resolves its
    ///     opcode to. See <see cref="ValidateShape" />.
    /// </exception>
    public byte[] EncodeClient(
        IExtensionClientPacket packet,
        Dialect dialect,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize) =>
        Encode(_clientTable, packet, dialect, flags, maxFrameSize);

    /// <summary>
    ///     Encodes an S-&gt;C extension packet into a frame stamped with
    ///     <paramref name="dialect" />, after checking that <paramref name="packet" /> is the
    ///     shape a peer resolves at that dialect.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="packet" /> is not the shape <paramref name="dialect" /> resolves its
    ///     opcode to. See <see cref="ValidateShape" />.
    /// </exception>
    public byte[] EncodeServer(
        IExtensionServerPacket packet,
        Dialect dialect,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize) =>
        Encode(_serverTable, packet, dialect, flags, maxFrameSize);

    /// <summary>
    ///     The shared encode body; the public entry points stay separate so the marker interfaces
    ///     keep direction a compile-time property.
    /// </summary>
    private static byte[] Encode(
        ExtensionDispatchTable table,
        IExtensionPacket packet,
        Dialect dialect,
        ExtensionFrameFlags flags,
        int maxFrameSize)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ValidateShape(table, packet, dialect);

        // WrittenSpan, not ToBody(): WriteFrame copies the body anyway, so the array would be a
        // second copy per packet.
        var writer = new PacketWriter();
        packet.WriteBody(writer);

        return ExtensionFrameCodec.WriteFrame(dialect, packet.Opcode, writer.WrittenSpan, flags,
            maxFrameSize);
    }

    /// <summary>
    ///     Refuses to stamp <paramref name="packet" /> with a dialect that does not resolve its
    ///     opcode to <paramref name="packet" />'s own type.
    /// </summary>
    /// <remarks>
    ///     A mismatched stamp is otherwise undetectable at send time and surfaces on the peer as
    ///     garbled fields rather than a protocol error. Throws
    ///     <see cref="InvalidOperationException" />, not <see cref="InvalidDataException" />: a
    ///     bad stamp is a defect in our own calling code, not untrusted input.
    /// </remarks>
    private static void ValidateShape(
        ExtensionDispatchTable table,
        IExtensionPacket packet,
        Dialect dialect)
    {
        var actual = packet.GetType();
        var resolved = table.ResolveType((byte)dialect, packet.Opcode);

        if (resolved is null)
            throw new InvalidOperationException(
                $"{actual.FullName} cannot be stamped with dialect 0x{(byte)dialect:X2}: no " +
                $"{table.Direction} extension packet is registered for opcode " +
                $"0x{packet.Opcode:X4} there, so no peer could decode it.");

        if (resolved != actual)
            throw new InvalidOperationException(
                $"{actual.FullName} cannot be stamped with dialect 0x{(byte)dialect:X2}: " +
                $"{table.Direction} opcode 0x{packet.Opcode:X4} resolves to {resolved.FullName} " +
                "at that dialect, so a peer would parse this body with the wrong shape.");
    }

    /// <summary>
    ///     Attempts to decode a single C-&gt;S extension frame from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if a complete frame was decoded; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <param name="buffer">A buffer that may hold zero, one, or more than one frame. Only the
    ///     first is decoded.</param>
    /// <param name="packet">The decoded packet, when the return value is true.</param>
    /// <param name="bytesConsumed">The bytes the frame occupied, when the return value is true.
    ///     Advance the buffer by this and call again to drain further frames.</param>
    /// <param name="expectedDialect">
    ///     The negotiated dialect. Pass an explicit <see langword="null" /> only before
    ///     negotiation has settled.
    /// </param>
    /// <param name="maxFrameSize">The largest total wire size accepted, length field included.</param>
    /// <remarks>
    ///     Code holding a <see cref="Negotiation.DialectResolution" /> should prefer
    ///     <see cref="ForConnection" />; code that knows its dialect without one should pass the
    ///     <see cref="Dialect" /> here rather than constructing a resolution to reach it.
    /// </remarks>
    /// <exception cref="InvalidDataException">The frame is malformed, no extension packet is
    ///     registered for its <c>(dialect, opcode)</c>, or its dialect differs from
    ///     <paramref name="expectedDialect" />.</exception>
    public bool TryDecodeClient(
        ReadOnlyMemory<byte> buffer,
        out IExtensionClientPacket? packet,
        out int bytesConsumed,
        Dialect? expectedDialect,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        var ok = TryDecode(_clientTable, buffer, out var decoded, out bytesConsumed,
            maxFrameSize, expectedDialect);
        packet = (IExtensionClientPacket?)decoded;

        return ok;
    }

    /// <summary>
    ///     Attempts to decode a single S-&gt;C extension frame from the front of
    ///     <paramref name="buffer" />.
    /// </summary>
    /// <returns><see langword="true" /> if a complete frame was decoded; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <param name="buffer">A buffer that may hold zero, one, or more than one frame. Only the
    ///     first is decoded.</param>
    /// <param name="packet">The decoded packet, when the return value is true.</param>
    /// <param name="bytesConsumed">The bytes the frame occupied, when the return value is true.</param>
    /// <param name="expectedDialect">
    ///     The negotiated dialect. Required and not defaulted, for the reason given on
    ///     <see cref="TryDecodeClient" /> - whose remarks also cover when to stay on this
    ///     low-level surface rather than binding a codec.
    /// </param>
    /// <param name="maxFrameSize">The largest total wire size accepted, length field included.</param>
    /// <exception cref="InvalidDataException">The frame is malformed, no extension packet is
    ///     registered for its <c>(dialect, opcode)</c>, or its dialect differs from
    ///     <paramref name="expectedDialect" />.</exception>
    public bool TryDecodeServer(
        ReadOnlyMemory<byte> buffer,
        out IExtensionServerPacket? packet,
        out int bytesConsumed,
        Dialect? expectedDialect,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        var ok = TryDecode(_serverTable, buffer, out var decoded, out bytesConsumed,
            maxFrameSize, expectedDialect);
        packet = (IExtensionServerPacket?)decoded;

        return ok;
    }

    /// <summary>
    ///     Binds this codec to a negotiated connection, producing a codec that stamps and
    ///     enforces <paramref name="resolution" />'s dialect on every frame with no per-call
    ///     choice to omit. This is the steady-state API; the per-call methods above are the
    ///     low-level surface beneath it. See <see cref="DialectCodec(ExtensionCodec, DialectResolution, int)" />
    ///     for the parameters and the constraint on <paramref name="resolution" />.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="resolution" /> did not engage a
    ///     dialect.</exception>
    public DialectCodec ForConnection(
        DialectResolution resolution,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize) =>
        new(this, resolution, maxFrameSize);

    private static bool TryDecode(
        ExtensionDispatchTable table,
        ReadOnlyMemory<byte> buffer,
        out IExtensionPacket? packet,
        out int bytesConsumed,
        int maxFrameSize,
        Dialect? expectedDialect)
    {
        packet = null;
        bytesConsumed = 0;

        // The framing layer owns the dialect check; it can refuse a foreign stamp before the
        // body is buffered.
        if (!ExtensionFrameCodec.TryReadFrame(buffer, out var header, out var body,
                out var consumed, (byte?)expectedDialect, maxFrameSize))
            return false;

        var decode = table.Resolve(header.DialectByte, header.Opcode)
            ?? throw new InvalidDataException(
                $"No registered {table.Direction} extension packet for opcode 0x{header.Opcode:X4} " +
                $"at dialect 0x{header.DialectByte:X2}.");

        // DALib's PacketReader throws InvalidOperationException past the end of a body. A filter,
        // not a blanket catch, so a parser defect is not mislabelled as peer input.
        try
        {
            packet = decode(body.Span);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                       or IndexOutOfRangeException or OverflowException
                                       or FormatException)
        {
            throw new InvalidDataException(
                $"Malformed body for {table.Direction} opcode 0x{header.Opcode:X4} at dialect " +
                $"0x{header.DialectByte:X2} ({body.Length} bytes): {ex.Message}", ex);
        }

        bytesConsumed = consumed;

        return true;
    }
}
