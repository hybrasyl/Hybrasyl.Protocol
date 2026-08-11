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
    /// <exception cref="ArgumentNullException"><paramref name="targetHost" /> is null.</exception>
    public static SslClientAuthenticationOptions ClientOptions(
        string targetHost,
        RemoteCertificateValidationCallback? validationCallback = null)
    {
        ArgumentNullException.ThrowIfNull(targetHost);

        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = Protocol,
            RemoteCertificateValidationCallback = validationCallback,
        };
    }
}
