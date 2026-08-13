namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     The native extension opcode registry - the codebase mirror of the authoritative table in
///     <c>docs/ALLOCATIONS.md</c>, which ships in this repository and in the NuGet package
///     alongside the code it governs.
/// </summary>
/// <remarks>
///     <para>
///         <b>Allocation discipline.</b> Opcodes are allocated as <em>exchanges</em>, not as
///         messages: a request and its response share one number across the two directions (the
///         direction split gives each side its own table, so this costs nothing). A request is
///         never answered at a different number. A packet with no response simply leaves its
///         number unused in the other direction.
///     </para>
///     <para>
///         Where both ends may initiate the same kind of exchange, each initiator gets its own
///         number - see <see cref="ClientEcho" /> and <see cref="ServerEcho" />. That is two
///         exchanges, not one exchange with an asymmetric reply.
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

    /// <summary>Client-initiated liveness exchange: probe C-&gt;S, reply S-&gt;C.</summary>
    public const ushort ClientEcho = 0x0100;

    /// <summary>Server-initiated liveness exchange: probe S-&gt;C, reply C-&gt;S.</summary>
    public const ushort ServerEcho = 0x0101;
}
