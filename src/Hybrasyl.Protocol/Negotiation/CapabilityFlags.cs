using System;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     Capability bits carried in the <see cref="CapabilityMarker" /> on the <c>0x7E</c> greeting.
///     All bits are reserved in marker v1 - the marker's mere <em>presence</em> is the capability
///     signal ("this server family speaks the extended protocol; upgrade to TLS to negotiate").
/// </summary>
/// <remarks>
///     The dialect range is deliberately <em>not</em> advertised here - it is negotiated inside
///     TLS (DialectOffer), so there is no plaintext downgrade surface. A likely future bit is
///     "TLS required" (the server declines a plaintext-dialect fallback); left reserved until the
///     server side needs it.
/// </remarks>
[Flags]
public enum CapabilityFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,
}
