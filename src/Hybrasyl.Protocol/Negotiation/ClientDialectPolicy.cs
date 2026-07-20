namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     A client's dialect policy: each client release implements <em>exactly one</em> dialect
///     version (its newest). Given a server's <see cref="DialectOffer" />, it resolves the
///     connection mode.
/// </summary>
/// <param name="Supported">The single dialect this client speaks.</param>
public readonly record struct ClientDialectPolicy(Dialect Supported)
{
    /// <summary>
    ///     Resolves the connection mode against <paramref name="offer" />: if this client's single
    ///     dialect is within the offered range, engage it
    ///     (<see cref="ConnectionMode.DialectOverTls" />); otherwise engage no dialect and send only
    ///     retail frames inside TLS (<see cref="ConnectionMode.RetailOverTls" />) - the client
    ///     cannot downgrade to a different dialect because it only implements one.
    /// </summary>
    public DialectResolution Resolve(DialectOffer offer) =>
        offer.Contains(Supported)
            ? DialectResolution.Engaged(Supported)
            : DialectResolution.RetailOverTls;
}
