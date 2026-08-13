using System;
using System.IO;
using Hybrasyl.Protocol.Framing;
using Hybrasyl.Protocol.Negotiation;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     An <see cref="ExtensionCodec" /> bound to one connection's negotiated dialect: it stamps
///     that dialect on everything it encodes and rejects anything that arrives stamped with
///     another. This is the steady-state API a consumer should hold once negotiation has settled.
/// </summary>
/// <remarks>
///     <para>
///         Cheap to construct and immutable; hold one per connection for its lifetime. It carries
///         no transport - encoding returns bytes and decoding consumes them, exactly as the
///         stateless codec does. The dialect is fixed at construction because a negotiated dialect
///         never changes mid-connection; a renegotiation is a new connection and a new instance.
///     </para>
/// </remarks>
public sealed class DialectCodec
{
    private readonly ExtensionCodec _codec;

    /// <summary>
    ///     Binds <paramref name="codec" /> to the dialect <paramref name="resolution" /> engaged.
    /// </summary>
    /// <param name="codec">The underlying stateless codec, which may be shared process-wide.</param>
    /// <param name="resolution">
    ///     The negotiated outcome. Must be <see cref="ConnectionMode.DialectOverTls" /> - the
    ///     retail modes carry <c>0xAA</c> frames, which are DALib's codec's business, not this
    ///     one's.
    /// </param>
    /// <param name="maxFrameSize">
    ///     The total-wire-size cap applied to every frame in both directions.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="resolution" /> did not engage a
    ///     dialect.</exception>
    /// <remarks>
    ///     <see cref="ExtensionCodec.ForConnection" /> is the usual way in; this constructor is
    ///     the primitive it calls.
    /// </remarks>
    public DialectCodec(
        ExtensionCodec codec,
        DialectResolution resolution,
        int maxFrameSize = ExtensionFrame.DefaultMaxFrameSize)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameSize);

        if (resolution.Mode != ConnectionMode.DialectOverTls || resolution.Dialect is not { } dialect)
            throw new ArgumentException(
                $"A dialect codec requires a resolution that engaged a dialect; {resolution.Mode} " +
                "carries retail framing, which travels on DALib's codec instead.",
                nameof(resolution));

        // DialectResolution's primary constructor is public, so an engaged resolution naming an
        // unallocatable dialect - 0xAA would stamp retail's own marker - bypasses Engaged.
        if (!ExtensionFrame.IsValidDialect((byte)dialect))
            throw new ArgumentException(
                $"A dialect codec cannot be bound to 0x{(byte)dialect:X2}: not an allocatable " +
                $"dialect (0x{ExtensionFrame.MinDialect:X2}.." +
                $"0x{ExtensionFrame.MaxDialect:X2}).",
                nameof(resolution));

        _codec = codec;
        Dialect = dialect;
        MaxFrameSize = maxFrameSize;
    }

    /// <summary>The dialect negotiated for this connection. Constant for its lifetime.</summary>
    public Dialect Dialect { get; }

    /// <summary>The total wire size cap applied to every frame in both directions.</summary>
    public int MaxFrameSize { get; }

    /// <summary>Encodes a C-&gt;S packet, stamped with the connection's dialect.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="packet" /> is not the shape
    ///     this connection's dialect resolves its opcode to.</exception>
    public byte[] EncodeClient(
        IExtensionClientPacket packet,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None) =>
        _codec.EncodeClient(packet, Dialect, flags, MaxFrameSize);

    /// <summary>Encodes an S-&gt;C packet, stamped with the connection's dialect.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="packet" /> is not the shape
    ///     this connection's dialect resolves its opcode to.</exception>
    public byte[] EncodeServer(
        IExtensionServerPacket packet,
        ExtensionFrameFlags flags = ExtensionFrameFlags.None) =>
        _codec.EncodeServer(packet, Dialect, flags, MaxFrameSize);

    /// <summary>
    ///     Attempts to decode a single C-&gt;S frame, enforcing the connection's dialect.
    /// </summary>
    /// <returns><see langword="true" /> if a complete frame was decoded; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The frame is malformed, stamped with a dialect
    ///     other than this connection's, or carries an unregistered opcode.</exception>
    public bool TryDecodeClient(
        ReadOnlyMemory<byte> buffer,
        out IExtensionClientPacket? packet,
        out int bytesConsumed) =>
        _codec.TryDecodeClient(buffer, out packet, out bytesConsumed, Dialect, MaxFrameSize);

    /// <summary>
    ///     Attempts to decode a single S-&gt;C frame, enforcing the connection's dialect.
    /// </summary>
    /// <returns><see langword="true" /> if a complete frame was decoded; <see langword="false" />
    ///     if more bytes are needed.</returns>
    /// <exception cref="InvalidDataException">The frame is malformed, stamped with a dialect
    ///     other than this connection's, or carries an unregistered opcode.</exception>
    public bool TryDecodeServer(
        ReadOnlyMemory<byte> buffer,
        out IExtensionServerPacket? packet,
        out int bytesConsumed) =>
        _codec.TryDecodeServer(buffer, out packet, out bytesConsumed, Dialect, MaxFrameSize);
}
