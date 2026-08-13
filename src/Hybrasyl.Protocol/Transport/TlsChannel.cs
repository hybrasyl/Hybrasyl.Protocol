using System;
using System.IO;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace Hybrasyl.Protocol.Transport;

/// <summary>
///     Performs the extension channel's TLS upgrade and enforces its one transport invariant:
///     the negotiated protocol is <see cref="TlsConfig.RequiredProtocol" />, or the connection
///     is dropped.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this exists rather than a pinned protocol in the options.</b> Requesting an
///         explicit protocol version is not portable: Apple's SecureTransport rejects an explicit
///         request above TLS 1.2, so pinning <c>Tls13</c> in the options throws
///         <c>PlatformNotSupportedException</c> on macOS before any certificate is examined, on
///         both ends. <see cref="TlsConfig" /> therefore leaves the enabled set at
///         <see cref="System.Security.Authentication.SslProtocols.None" /> - the platform's own
///         best choice - and the requirement is checked here, after the handshake, at the one
///         point where the answer actually exists.
///     </para>
///     <para>
///         This is stronger than pinning, not a concession to it. A pinned request is a request
///         the platform may decline to express; a postcondition holds on every platform, and it
///         also catches the case a pin cannot - a stack that silently negotiates something older.
///         No application data has crossed at the point of the check, so a refused connection
///         leaks nothing.
///     </para>
///     <para>
///         <b>Bound the handshake.</b> Neither <c>SslStream</c> nor the negotiation that follows
///         bounds itself, so a peer that advertises capability and then stalls would block
///         indefinitely. Pass a cancellation token carrying a deadline. The
///         library deliberately does not choose the number: how long is too long is a deployment
///         question, like the frame-size cap.
///     </para>
/// </remarks>
public static class TlsChannel
{
    /// <summary>
    ///     Upgrades <paramref name="innerStream" /> to TLS as the client, then verifies the
    ///     negotiated protocol.
    /// </summary>
    /// <param name="innerStream">The connected plaintext stream, positioned at the trigger byte.</param>
    /// <param name="options">Options from <see cref="TlsConfig.ClientOptions" />.</param>
    /// <param name="leaveInnerStreamOpen">Whether disposing the returned stream leaves
    ///     <paramref name="innerStream" /> open.</param>
    /// <param name="cancellationToken">Carries the handshake deadline; see the remarks on
    ///     <see cref="TlsChannel" />.</param>
    /// <returns>The authenticated stream, guaranteed to be running
    ///     <see cref="TlsConfig.RequiredProtocol" />.</returns>
    /// <exception cref="AuthenticationException">The handshake failed, or completed on a protocol
    ///     other than <see cref="TlsConfig.RequiredProtocol" />.</exception>
    public static async Task<SslStream> UpgradeAsClientAsync(
        Stream innerStream,
        SslClientAuthenticationOptions options,
        bool leaveInnerStreamOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        ArgumentNullException.ThrowIfNull(options);

        var ssl = new SslStream(innerStream, leaveInnerStreamOpen);

        try
        {
            await ssl.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            VerifyNegotiatedProtocol(ssl);
        }
        catch
        {
            // The channel is unusable either way, and a half-open TLS stream is worse than none.
            await ssl.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        return ssl;
    }

    /// <summary>
    ///     Upgrades <paramref name="innerStream" /> to TLS as the server, then verifies the
    ///     negotiated protocol.
    /// </summary>
    /// <param name="innerStream">The connected plaintext stream, positioned at the trigger byte.</param>
    /// <param name="options">Options from <see cref="TlsConfig.ServerOptions(System.Security.Cryptography.X509Certificates.X509Certificate2)" />.</param>
    /// <param name="leaveInnerStreamOpen">Whether disposing the returned stream leaves
    ///     <paramref name="innerStream" /> open.</param>
    /// <param name="cancellationToken">Carries the handshake deadline; see the remarks on
    ///     <see cref="TlsChannel" />.</param>
    /// <returns>The authenticated stream, guaranteed to be running
    ///     <see cref="TlsConfig.RequiredProtocol" />.</returns>
    /// <exception cref="AuthenticationException">The handshake failed, or completed on a protocol
    ///     other than <see cref="TlsConfig.RequiredProtocol" />.</exception>
    public static async Task<SslStream> UpgradeAsServerAsync(
        Stream innerStream,
        SslServerAuthenticationOptions options,
        bool leaveInnerStreamOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        ArgumentNullException.ThrowIfNull(options);

        var ssl = new SslStream(innerStream, leaveInnerStreamOpen);

        try
        {
            await ssl.AuthenticateAsServerAsync(options, cancellationToken).ConfigureAwait(false);
            VerifyNegotiatedProtocol(ssl);
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        return ssl;
    }

    /// <summary>
    ///     Throws unless <paramref name="stream" /> negotiated
    ///     <see cref="TlsConfig.RequiredProtocol" />. Call this after any handshake performed
    ///     without <see cref="UpgradeAsClientAsync" /> / <see cref="UpgradeAsServerAsync" /> - the
    ///     options alone cannot enforce it.
    /// </summary>
    /// <exception cref="AuthenticationException">A different protocol was negotiated, or
    ///     <paramref name="stream" /> has not completed a handshake at all.</exception>
    public static void VerifyNegotiatedProtocol(SslStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // SslProtocol throws InvalidOperationException before authentication, so checking here is
        // what keeps a check-too-early from surfacing as an unrelated-looking failure.
        if (!stream.IsAuthenticated)
            throw new AuthenticationException(
                "The TLS protocol cannot be verified before the handshake has completed; call " +
                "this after authenticating.");

        if (stream.SslProtocol == TlsConfig.RequiredProtocol)
            return;

        throw new AuthenticationException(
            $"The TLS handshake negotiated {stream.SslProtocol}, but the extension channel " +
            $"requires {TlsConfig.RequiredProtocol}. Where a platform's TLS stack cannot " +
            "negotiate it, the extension channel is unavailable and retail framing remains." +
            BackendHint(
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                AppContext.TryGetSwitch(NetworkFrameworkSwitch, out var enabled) && enabled));
    }

    /// <summary>
    ///     The .NET switch selecting a macOS TLS backend that can negotiate TLS 1.3. The default
    ///     backend caps at TLS 1.2, so a macOS client leaving this unset fails the postcondition
    ///     with nothing on the wire to explain why.
    /// </summary>
    /// <remarks>
    ///     Set it through <c>RuntimeHostConfigurationOption</c> in the project file rather than
    ///     <see cref="AppContext.SetSwitch" /> in code: it is read when <c>SslStream</c> first
    ///     initialises its backend, so a call sequenced after that point is ignored without
    ///     complaint.
    /// </remarks>
    public const string NetworkFrameworkSwitch = "System.Net.Security.UseNetworkFramework";

    /// <summary>
    ///     The platform-specific tail of the failure message. Separated from the ambient checks so
    ///     every branch is reachable in a test on any host.
    /// </summary>
    internal static string BackendHint(bool isMacOs, bool networkFrameworkEnabled)
    {
        if (!isMacOs)
            return string.Empty;

        if (networkFrameworkEnabled)
            return " This is macOS with a TLS 1.3-capable backend already selected, so the peer " +
                   "is the constraint - note that a macOS *server* caps at TLS 1.2 regardless of " +
                   "this setting.";

        return $" On macOS the default backend caps at TLS 1.2. Set the '{NetworkFrameworkSwitch}' " +
               "switch, via RuntimeHostConfigurationOption in the project file so it applies before " +
               "SslStream initialises, to select a backend that negotiates TLS 1.3. It governs " +
               "client connections only; a macOS server cannot negotiate TLS 1.3 at all.";
    }
}
