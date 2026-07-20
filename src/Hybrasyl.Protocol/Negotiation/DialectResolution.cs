namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The outcome of resolving a connection against the version policy: the
///     <see cref="ConnectionMode" /> and, for <see cref="ConnectionMode.DialectOverTls" />, the
///     engaged <see cref="Dialect" /> namespace (null for the retail modes).
/// </summary>
/// <param name="Mode">The resolved connection mode.</param>
/// <param name="Dialect">The engaged dialect, or null for a retail mode.</param>
public readonly record struct DialectResolution(ConnectionMode Mode, Dialect? Dialect)
{
    /// <summary>A TLS connection with the <paramref name="dialect" /> namespace engaged.</summary>
    public static DialectResolution Engaged(Dialect dialect) =>
        new(ConnectionMode.DialectOverTls, dialect);

    /// <summary>Retail framing inside TLS (below/outside the dialect range).</summary>
    public static DialectResolution RetailOverTls { get; } =
        new(ConnectionMode.RetailOverTls, null);

    /// <summary>Retail framing on a plaintext socket (no TLS upgrade occurred).</summary>
    public static DialectResolution PlaintextRetail { get; } =
        new(ConnectionMode.PlaintextRetail, null);
}
