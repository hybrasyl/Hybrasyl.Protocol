using System;

namespace Hybrasyl.Protocol.Framing;

/// <summary>
///     The per-frame <c>flags</c> byte. All bits are reserved in dialect v1.
/// </summary>
/// <remarks>
///     <c>bit0</c> is reserved for a future per-frame compression opt-in (compression is out for
///     v1 - TLS 1.3 removed record compression because of CRIME/BREACH, so any future scheme is
///     an explicit, dictionary-free per-frame choice). The remaining bits are reserved and must
///     be written as zero.
/// </remarks>
[Flags]
public enum ExtensionFrameFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,
}
