using System;
using Hybrasyl.Protocol.Framing;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     A server's dialect policy: the contiguous range of dialects
///     <c>[Floor..Ceiling]</c> it supports and advertises. Raising <see cref="Floor" /> retires
///     old dialects permanently.
/// </summary>
/// <param name="Floor">The lowest supported dialect.</param>
/// <param name="Ceiling">The highest supported dialect.</param>
public readonly record struct ServerDialectPolicy(Dialect Floor, Dialect Ceiling)
{
    /// <summary>
    ///     Creates a validated policy. Prefer this over the primary constructor - it rejects both
    ///     an inverted range and a bound that is not an allocatable dialect.
    /// </summary>
    /// <remarks>
    ///     <see cref="Dialect" /> is a <c>byte</c>-backed enum, so an unallocated value is a cast
    ///     away and a policy built from one would advertise a range no peer can accept. Same
    ///     invariants <see cref="DialectOffer" /> enforces on the wire, but as an
    ///     <see cref="ArgumentException" />: a bad policy is a caller defect, a bad offer is
    ///     untrusted input.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="floor" /> exceeds
    ///     <paramref name="ceiling" />, or either is outside <c>0xB0</c>..<c>0xFE</c>.</exception>
    public static ServerDialectPolicy Create(Dialect floor, Dialect ceiling)
    {
        if (!ExtensionFrame.IsValidDialect((byte)floor))
            throw new ArgumentException(
                $"Dialect floor 0x{(byte)floor:X2} is not an allocatable dialect " +
                $"(valid 0x{ExtensionFrame.MinDialect:X2}..0x{ExtensionFrame.MaxDialect:X2}).",
                nameof(floor));

        if (!ExtensionFrame.IsValidDialect((byte)ceiling))
            throw new ArgumentException(
                $"Dialect ceiling 0x{(byte)ceiling:X2} is not an allocatable dialect " +
                $"(valid 0x{ExtensionFrame.MinDialect:X2}..0x{ExtensionFrame.MaxDialect:X2}).",
                nameof(ceiling));

        if ((byte)floor > (byte)ceiling)
            throw new ArgumentException(
                $"Dialect floor 0x{(byte)floor:X2} exceeds ceiling 0x{(byte)ceiling:X2}.",
                nameof(floor));

        return new ServerDialectPolicy(floor, ceiling);
    }

    /// <summary>The offer this server advertises inside TLS.</summary>
    public DialectOffer ToOffer() => new(Floor, Ceiling);

    /// <summary>True if <paramref name="dialect" /> is within this server's supported range.</summary>
    public bool Supports(Dialect dialect) => ToOffer().Contains(dialect);

    /// <summary>
    ///     Resolves the server's view of a client that chose <paramref name="dialect" />: the
    ///     dialect namespace is engaged if supported, otherwise the connection stays retail-only
    ///     inside TLS.
    /// </summary>
    public DialectResolution Resolve(Dialect dialect) =>
        Supports(dialect)
            ? DialectResolution.Engaged(dialect)
            : DialectResolution.RetailOverTls;
}
