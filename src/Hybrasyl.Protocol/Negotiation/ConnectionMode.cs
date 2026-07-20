namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The character of a connection. Note this is <em>not</em> a per-frame framing choice: when a
///     dialect is engaged, retail <c>0xAA</c> frames (for un-migrated packets) and extension
///     <c>0xB0+</c> frames (for new/replaced packets) coexist on the same stream, sorted per frame
///     by the <see cref="Framing.FrameRouter" />. This enum describes whether TLS is up and whether
///     an extension dialect namespace is active - not which framing a given frame uses.
/// </summary>
public enum ConnectionMode
{
    /// <summary>
    ///     A plaintext socket carrying retail <c>0xAA</c> frames - a retail client, or a
    ///     Hybrasyl-family server talking to one. Decided <em>before</em> any TLS upgrade (the
    ///     client never sent a <c>ClientHello</c>), so it is not produced by the dialect resolver.
    /// </summary>
    PlaintextRetail,

    /// <summary>
    ///     TLS is up but no extension dialect is engaged - the client upgraded but its single
    ///     supported dialect is outside the server's advertised range, so only retail <c>0xAA</c>
    ///     frames flow (retail semantics on an encrypted transport).
    /// </summary>
    RetailOverTls,

    /// <summary>
    ///     TLS is up and an extension dialect namespace is engaged. Extension <c>0xB0+</c> frames
    ///     for new/replaced packets flow alongside retail <c>0xAA</c> frames for everything not yet
    ///     migrated.
    /// </summary>
    DialectOverTls,
}
