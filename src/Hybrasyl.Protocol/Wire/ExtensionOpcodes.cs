namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     The native extension opcode registry - the codebase mirror of the authoritative table in
///     <c>EXTENSIONS.md</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>Allocation discipline.</b> Opcodes are allocated as <em>exchanges</em>: a
///         request/response pair shares one number across the two directions (the direction split
///         gives each side its own table, so this costs nothing). A packet with no response simply
///         leaves its number unused in the other direction.
///     </para>
///     <para>
///         <b>Category blocks.</b> Native space is allocated IANA-style from 64-opcode,
///         <c>0x40</c>-aligned category blocks (<c>opcode &gt;&gt; 6</c> is the block index). A
///         category that outgrows its block is granted another block, never renumbered.
///         <c>0x0000</c>-<c>0x00FF</c> (blocks 0-3) is the retail-mirror space for 1:1
///         replacements; a retail <em>variant family</em> instead explodes into its own category
///         block up here, one opcode per variant. Native allocation starts at <c>0x0100</c>
///         (block 4), the system/infrastructure category (<c>0x0100</c>-<c>0x013F</c>).
///     </para>
/// </remarks>
public static class ExtensionOpcodes
{
    // System / infrastructure: 0x0100-0x013F

    /// <summary>Liveness probe, either direction; the peer answers with an
    ///     <see cref="Pong" /> echoing the token.</summary>
    public const ushort Ping = 0x0100;

    /// <summary>Liveness reply, either direction; echoes the <see cref="Ping" /> token.</summary>
    public const ushort Pong = 0x0101;
}
