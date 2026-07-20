using System;

namespace Hybrasyl.Protocol.Framing;

/// <summary>
///     Wire-layout constants for an extended-framing frame:
///     <c>[u32-BE length] [u8 signature] [u16-BE opcode] [u8 flags] [body]</c>.
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
///         one-time, permanent choice for the whole extension protocol. Being big-endian and
///         bounded by <see cref="DefaultMaxFrameSize" />, an extension frame's first byte is
///         always <c>0x00</c>; a retail frame's is always <c>0xAA</c>. That is what lets a reader
///         route by the first byte (<c>0xAA</c> -&gt; DALib retail codec, otherwise -&gt; the
///         extension codec) even though the <c>0xB0+</c> dialect signature sits at offset 4.
///     </para>
/// </remarks>
public static class ExtensionFrame
{
    /// <summary>Lowest dialect signature. <c>0xB0</c> = <see cref="Dialect.V1" />.</summary>
    public const byte MinSignature = 0xB0;

    /// <summary>Highest allocatable dialect signature. <c>0xFF</c> is never allocated.</summary>
    public const byte MaxSignature = 0xFE;

    /// <summary>Size of the leading <c>u32</c> length field.</summary>
    public const int LengthFieldLength = 4;

    /// <summary>Size of the <c>u8</c> signature field.</summary>
    public const int SignatureLength = 1;

    /// <summary>Size of the <c>u16</c> opcode field.</summary>
    public const int OpcodeLength = 2;

    /// <summary>Size of the <c>u8</c> flags field.</summary>
    public const int FlagsLength = 1;

    /// <summary>
    ///     Number of bytes the <c>length</c> field counts: everything after the length field
    ///     itself (signature + opcode + flags + body). A body-less frame carries
    ///     <see cref="MinLengthValue" />.
    /// </summary>
    public const int HeaderAfterLengthLength = SignatureLength + OpcodeLength + FlagsLength;

    /// <summary>Total fixed prefix before the body: length + signature + opcode + flags.</summary>
    public const int HeaderLength = LengthFieldLength + HeaderAfterLengthLength;

    /// <summary>
    ///     The smallest legal value of the <c>length</c> field: it must cover at least the
    ///     signature, opcode, and flags of a body-less frame.
    /// </summary>
    public const int MinLengthValue = HeaderAfterLengthLength;

    /// <summary>
    ///     Default maximum accepted frame size (1 MiB). A length prefix is an allocation
    ///     primitive, so a claimed length is validated against this cap <em>before</em> any
    ///     buffering or allocation and a violation is fatal to the connection. Tunable, but
    ///     required at any field width - it is what keeps <c>u32</c>'s theoretical 4 GiB from
    ///     ever materialising.
    /// </summary>
    public const int DefaultMaxFrameSize = 1 << 20;

    /// <summary>
    ///     Returns <see langword="true" /> if <paramref name="signature" /> is a valid,
    ///     allocatable dialect signature (<c>0xB0</c>..<c>0xFE</c>).
    /// </summary>
    public static bool IsDialectSignature(byte signature) =>
        signature is >= MinSignature and <= MaxSignature;
}
