namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The outcome of resolving a connection against the version policy: the
///     <see cref="ConnectionMode" /> and, for <see cref="ConnectionMode.ExtensionOverTls" />, the
///     negotiated <see cref="Dialect" /> (null for the retail modes).
/// </summary>
/// <param name="Mode">The resolved connection mode.</param>
/// <param name="Dialect">The negotiated dialect, or null for a retail mode.</param>
public readonly record struct DialectResolution(ConnectionMode Mode, Dialect? Dialect)
{
    /// <summary>An extension connection speaking <paramref name="dialect" />.</summary>
    public static DialectResolution Extension(Dialect dialect) =>
        new(ConnectionMode.ExtensionOverTls, dialect);

    /// <summary>Retail framing inside TLS (below/outside the dialect range).</summary>
    public static DialectResolution RetailOverTls { get; } =
        new(ConnectionMode.RetailOverTls, null);

    /// <summary>Retail framing on a plaintext socket (no TLS upgrade occurred).</summary>
    public static DialectResolution PlaintextRetail { get; } =
        new(ConnectionMode.PlaintextRetail, null);
}
