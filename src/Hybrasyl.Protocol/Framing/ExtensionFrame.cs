using System;

namespace Hybrasyl.Protocol.Framing;

/// <summary>
///     Wire-layout constants for an extended-framing frame:
///     <c>[u32-BE length] [u8 dialect] [u16-BE opcode] [u8 flags] [body]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Extension frames travel <em>inside</em> the negotiated TLS 1.3 stream, so
///         confidentiality, integrity, and replay protection are TLS's job - a frame carries no
///         crypto of its own. The <c>length</c> prefix exists only because <c>SslStream</c> hands
///         us a byte <em>stream</em>, not message-aligned records.
///     </para>
///     <para>
///         <b>Length is first and big-endian by design.</b> It is read before any other field, so
///         unlike every other field it cannot be re-versioned by the dialect mechanism - it is a
///         one-time, permanent choice for the whole extension protocol. Being big-endian, an
///         extension frame's first byte cannot be <c>0xAA</c> at any realistic size (that would
///         need a length of <c>0xAA000000</c>, ~2.66 GiB), while a retail frame's always is. That
///         is what lets a reader route by the first byte (<c>0xAA</c> -&gt; DALib retail codec,
///         otherwise -&gt; the extension codec) even though the <c>0xB0+</c> dialect sits
///         at offset 4. Note the guarantee is structural, not a consequence of
///         <see cref="DefaultMaxFrameSize" /> - see <see cref="FrameRouter" />.
///     </para>
/// </remarks>
public static class ExtensionFrame
{
    /// <summary>Lowest dialect. <c>0xB0</c> = <see cref="Dialect.V1" />.</summary>
    public const byte MinDialect = 0xB0;

    /// <summary>Highest allocatable dialect. <c>0xFF</c> is never allocated.</summary>
    public const byte MaxDialect = 0xFE;

    /// <summary>Size of the leading <c>u32</c> length field.</summary>
    public const int LengthFieldLength = 4;

    /// <summary>Size of the <c>u8</c> dialect field.</summary>
    public const int DialectLength = 1;

    /// <summary>Size of the <c>u16</c> opcode field.</summary>
    public const int OpcodeLength = 2;

    /// <summary>Size of the <c>u8</c> flags field.</summary>
    public const int FlagsLength = 1;

    /// <summary>
    ///     Number of bytes the <c>length</c> field counts: everything after the length field
    ///     itself (dialect + opcode + flags + body). A body-less frame carries
    ///     <see cref="MinLengthValue" />.
    /// </summary>
    public const int HeaderAfterLengthLength = DialectLength + OpcodeLength + FlagsLength;

    /// <summary>Total fixed prefix before the body: length + dialect + opcode + flags.</summary>
    public const int HeaderLength = LengthFieldLength + HeaderAfterLengthLength;

    /// <summary>
    ///     The smallest legal value of the <c>length</c> field: it must cover at least the
    ///     dialect, opcode, and flags of a body-less frame.
    /// </summary>
    public const int MinLengthValue = HeaderAfterLengthLength;

    /// <summary>
    ///     Default maximum accepted frame size (8 MiB). A length prefix is an allocation
    ///     primitive, so a claimed length is validated against this cap <em>before</em> any
    ///     buffering or allocation and a violation is fatal to the connection. Tunable, but
    ///     required at any field width - it is what keeps <c>u32</c>'s theoretical 4 GiB from
    ///     ever materialising.
    /// </summary>
    public const int DefaultMaxFrameSize = 1 << 23;

    /// <summary>
    ///     The set of <c>flags</c> bits any dialect currently defines: none. Every bit is
    ///     reserved in v1 and must be written as zero, so a frame carrying any bit outside this
    ///     mask is malformed and is rejected by both the reader and the writer.
    /// </summary>
    /// <remarks>
    ///     Validity becomes dialect-dependent once a dialect defines a bit, since a frame
    ///     stamped v1 must still reject a bit a later dialect defines.
    /// </remarks>
    public static readonly byte DefinedFlagsMask = 0x00;

    /// <summary>
    ///     Returns <see langword="true" /> if <paramref name="dialect" /> is a valid,
    ///     allocatable dialect (<c>0xB0</c>..<c>0xFE</c>).
    /// </summary>
    public static bool IsValidDialect(byte dialect) =>
        dialect is >= MinDialect and <= MaxDialect;

    /// <summary>
    ///     Returns <see langword="true" /> if <paramref name="flags" /> sets only bits some
    ///     dialect defines - i.e. in v1, no bits at all.
    /// </summary>
    public static bool IsDefinedFlags(ExtensionFrameFlags flags) =>
        ((byte)flags & ~DefinedFlagsMask) == 0;
}
