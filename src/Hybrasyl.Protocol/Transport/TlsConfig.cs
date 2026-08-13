using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Hybrasyl.Protocol.Transport;

/// <summary>
///     The shared TLS configuration for the extension channel, pinned in one place so the server
///     and Brigid cannot drift: <b>TLS 1.3 only</b>, no legacy fallback (we own both ends).
///     Certificate <em>trust</em> policy (system roots, TOFU pinning) is the client application's
///     concern, supplied via the validation callback.
/// </summary>
public static class TlsConfig
{
    /// <summary>The only protocol version the extension channel speaks.</summary>
    public const SslProtocols Protocol = SslProtocols.Tls13;

    // The server overloads leave CertificateRevocationCheckMode at NoCheck: it governs the
    // client's certificate, and ClientCertificateRequired is false, so no client chain is built.

    /// <summary>Server-side <c>SslStream</c> options: TLS 1.3 only, no client certificate.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="certificate" /> is null.</exception>
    public static SslServerAuthenticationOptions ServerOptions(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            EnabledSslProtocols = Protocol,
            ClientCertificateRequired = false,
        };
    }

    /// <summary>
    ///     Server-side <c>SslStream</c> options carrying a full certificate context, for
    ///     serving intermediates (a chain file) rather than a bare leaf: TLS 1.3 only, no
    ///     client certificate.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="certificateContext" /> is null.</exception>
    public static SslServerAuthenticationOptions ServerOptions(
        SslStreamCertificateContext certificateContext)
    {
        ArgumentNullException.ThrowIfNull(certificateContext);

        return new SslServerAuthenticationOptions
        {
            ServerCertificateContext = certificateContext,
            EnabledSslProtocols = Protocol,
            ClientCertificateRequired = false,
        };
    }

    /// <summary>
    ///     Client-side <c>SslStream</c> options: TLS 1.3 only. A null
    ///     <paramref name="validationCallback" /> means platform default (system-root) validation;
    ///     the TOFU flow supplies its own callback.
    /// </summary>
    /// <remarks>
    ///     <strong><paramref name="targetHost" /> must be a real host name.</strong> It is what
    ///     the platform validates the presented certificate against, so an empty or whitespace
    ///     value removes the identity check rather than relaxing it: the handshake still succeeds,
    ///     still encrypts, and still reports no error, but <em>any</em> certificate the chain
    ///     accepts will do - which is precisely the MITM this channel exists to prevent. .NET
    ///     permits an empty <c>TargetHost</c> only alongside a callback that validates the subject
    ///     itself, and this overload's callback is optional.
    /// </remarks>
    /// <param name="targetHost">
    ///     The host name the presented certificate is validated against. Must be a real name; see
    ///     the remarks.
    /// </param>
    /// <param name="validationCallback">
    ///     Trust policy. Null means platform default (system-root) validation; the TOFU flow
    ///     supplies its own.
    /// </param>
    /// <param name="revocationMode">
    ///     Whether the chain is checked for revocation. <strong>Defaults to
    ///     <see cref="X509RevocationMode.Online" />, which is not .NET's default</strong> -
    ///     <see cref="SslClientAuthenticationOptions" /> leaves this at
    ///     <see cref="X509RevocationMode.NoCheck" />, so a revoked server certificate would
    ///     otherwise validate cleanly for the rest of its stated lifetime. Since the documented
    ///     default trust path here is public-CA system-root validation, revocation is the only
    ///     mechanism that retires a compromised certificate before it expires, and silently not
    ///     doing it would make that path weaker than it reads.
    ///     <para>
    ///         Pass <see cref="X509RevocationMode.NoCheck" /> deliberately where it is right - the
    ///         TOFU/self-signed path, where there is no CRL or OCSP responder to ask and the check
    ///         only costs a doomed network round trip, and offline or air-gapped deployments where
    ///         availability genuinely outranks revocation enforcement.
    ///     </para>
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="targetHost" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="targetHost" /> is empty or
    ///     whitespace.</exception>
    public static SslClientAuthenticationOptions ClientOptions(
        string targetHost,
        RemoteCertificateValidationCallback? validationCallback = null,
        X509RevocationMode revocationMode = X509RevocationMode.Online)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);

        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = Protocol,
            RemoteCertificateValidationCallback = validationCallback,
            CertificateRevocationCheckMode = revocationMode,
        };
    }
}
