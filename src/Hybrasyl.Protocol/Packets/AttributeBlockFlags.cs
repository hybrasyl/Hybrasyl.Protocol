using System;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The <c>u32</c> word leading an <see cref="Attributes" /> body, selecting which blocks
///     follow. Blocks appear in ascending bit order.
/// </summary>
/// <remarks>
///     <para>
///         This selects blocks and <em>nothing else</em>. Retail's flag byte also carried
///         standalone state - an unread-mail indicator and a movement mode - which live in
///         <see cref="AttributesStatus" /> here.
///     </para>
///     <para>
///         <b>A bit outside this set is a protocol error, not a skip.</b> A fixed-size block
///         cannot be skipped without knowing its size, so a receiver seeing an unfamiliar bit has
///         no way to find the next block. That is the opposite of the rule for an unrecognised
///         extended-stat id, which is skippable because every record is a fixed 10 bytes.
///     </para>
/// </remarks>
[Flags]
public enum AttributeBlockFlags : uint
{
    /// <summary>No block present.</summary>
    None = 0x00000000,

    /// <summary><see cref="AttributesPrimary" /> follows.</summary>
    Primary = 0x00000001,

    /// <summary><see cref="AttributesVitals" /> follows.</summary>
    Vitals = 0x00000002,

    /// <summary><see cref="AttributesExperience" /> follows.</summary>
    Experience = 0x00000004,

    /// <summary><see cref="AttributesCombat" /> follows.</summary>
    Combat = 0x00000008,

    /// <summary><see cref="AttributesStatus" /> follows.</summary>
    Status = 0x00000010,

    /// <summary>An extended-stat snapshot follows.</summary>
    ExtendedStats = 0x00000020
}
