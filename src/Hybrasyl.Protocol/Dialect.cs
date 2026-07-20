namespace Hybrasyl.Protocol;

/// <summary>
///     A Hybrasyl extended-framing dialect version. The wire <em>signature byte is the dialect
///     version</em>: there is exactly one signature per dialect, allocated upward from
///     <c>0xB0</c>. Retail DOOMVAS v1 framing (<c>0xAA</c>) is not a dialect and is not
///     represented here - it is DALib's domain.
/// </summary>
/// <remarks>
///     <para>
///         A server advertises a contiguous supported range <c>[floor..ceiling]</c> inside the
///         negotiated TLS channel; raising the floor retires old dialects permanently. This is
///         the single version axis the protocol turns on - not a capability set.
///     </para>
///     <para>
///         <c>0xFF</c> is never allocated. <c>0xAB</c>-<c>0xAF</c> are left unallocated as a
///         buffer above retail's <c>0xAA</c>.
///     </para>
/// </remarks>
public enum Dialect : byte
{
    /// <summary>Dialect version 1 - wire signature <c>0xB0</c>.</summary>
    V1 = 0xB0,
}
