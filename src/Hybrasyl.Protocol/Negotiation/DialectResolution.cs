using System;
using Hybrasyl.Protocol.Framing;

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
    /// <remarks>
    ///     <paramref name="dialect" /> must be an allocatable dialect: <see cref="Dialect" /> is
    ///     a <c>byte</c>-backed enum, so <c>(Dialect)0xAA</c> - retail's framing marker - is a cast
    ///     away, and a resolution carrying it would describe a connection that cannot exist.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="dialect" /> is not an allocatable
    ///     dialect.</exception>
    public static DialectResolution Engaged(Dialect dialect)
    {
        if (!ExtensionFrame.IsValidDialect((byte)dialect))
            throw new ArgumentException(
                $"Dialect 0x{(byte)dialect:X2} is not an allocatable dialect " +
                $"(0x{ExtensionFrame.MinDialect:X2}..0x{ExtensionFrame.MaxDialect:X2}), so " +
                "no connection can have engaged it.",
                nameof(dialect));

        return new DialectResolution(ConnectionMode.DialectOverTls, dialect);
    }

    /// <summary>Retail framing inside TLS (below/outside the dialect range).</summary>
    public static DialectResolution RetailOverTls { get; } =
        new(ConnectionMode.RetailOverTls, null);

    /// <summary>Retail framing on a plaintext socket (no TLS upgrade occurred).</summary>
    public static DialectResolution PlaintextRetail { get; } =
        new(ConnectionMode.PlaintextRetail, null);
}
