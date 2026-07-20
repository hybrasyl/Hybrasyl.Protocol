using System;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     A server's dialect policy: the contiguous range of dialect signatures
///     <c>[Floor..Ceiling]</c> it supports and advertises. Raising <see cref="Floor" /> retires
///     old dialects permanently.
/// </summary>
/// <param name="Floor">The lowest supported dialect.</param>
/// <param name="Ceiling">The highest supported dialect.</param>
public readonly record struct ServerDialectPolicy(Dialect Floor, Dialect Ceiling)
{
    /// <summary>
    ///     Creates a validated policy. Prefer this over the primary constructor - it rejects an
    ///     inverted range.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="floor" /> exceeds
    ///     <paramref name="ceiling" />.</exception>
    public static ServerDialectPolicy Create(Dialect floor, Dialect ceiling)
    {
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
    ///     Resolves the server's view of a client that chose <paramref name="dialect" />: an
    ///     extension connection if the dialect is supported, otherwise retail framing inside TLS.
    /// </summary>
    public DialectResolution Resolve(Dialect dialect) =>
        Supports(dialect)
            ? DialectResolution.Extension(dialect)
            : DialectResolution.RetailOverTls;
}
