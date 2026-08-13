namespace Hybrasyl.Protocol.Framing;

/// <summary>
///     The parsed header of one extension frame: its dialect <paramref name="DialectByte" />, the
///     <paramref name="Opcode" />, and its <paramref name="Flags" />. The frame length is not
///     retained - the body is returned separately as a slice.
/// </summary>
/// <param name="DialectByte">The dialect byte (<c>0xB0</c>..<c>0xFE</c>).</param>
/// <param name="Opcode">The <c>u16</c> opcode identifying the packet within the dialect.</param>
/// <param name="Flags">The per-frame flags byte.</param>
public readonly record struct ExtensionFrameHeader(
    byte DialectByte,
    ushort Opcode,
    ExtensionFrameFlags Flags)
{
    /// <summary>The dialect this frame belongs to, i.e. <see cref="DialectByte" /> as a
    ///     <see cref="Dialect" />.</summary>
    public Dialect Dialect => (Dialect)DialectByte;
}
