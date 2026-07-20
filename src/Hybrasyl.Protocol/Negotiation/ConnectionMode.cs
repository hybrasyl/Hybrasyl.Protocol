namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The three coherent modes a connection can end up in. The axes <em>TLS-or-not</em> and
///     <em>which framing rides it</em> are orthogonal; only these three combinations occur.
/// </summary>
public enum ConnectionMode
{
    /// <summary>
    ///     Retail <c>0xAA</c> framing on a plaintext socket - a retail client, or a
    ///     Hybrasyl-family server talking to one. Decided <em>before</em> any TLS upgrade (the
    ///     client never sent a <c>ClientHello</c>), so it is not produced by the dialect resolver.
    /// </summary>
    PlaintextRetail,

    /// <summary>
    ///     Retail <c>0xAA</c> framing inside TLS - a client that upgraded but whose single
    ///     supported dialect is outside the server's advertised range. Retail semantics on an
    ///     encrypted transport.
    /// </summary>
    RetailOverTls,

    /// <summary>
    ///     A negotiated extension dialect (<c>0xB0+</c> framing) inside TLS.
    /// </summary>
    ExtensionOverTls,
}
