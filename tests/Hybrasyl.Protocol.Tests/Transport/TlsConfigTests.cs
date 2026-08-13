using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hybrasyl.Protocol.Transport;

namespace Hybrasyl.Protocol.Tests.Transport;

public class TlsConfigTests
{
    private static X509Certificate2 CreateTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=sreang-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public void ServerOptions_LeaveProtocolToThePlatform_CarryCert_RequireNoClientCert()
    {
        using var certificate = CreateTestCertificate();

        var options = TlsConfig.ServerOptions(certificate);

        options.EnabledSslProtocols.Should().Be(SslProtocols.None,
            "an explicit request above TLS 1.2 is refused outright by some platforms; the " +
            "requirement is enforced after the handshake instead");
        options.ServerCertificate.Should().BeSameAs(certificate);
        options.ClientCertificateRequired.Should().BeFalse();
    }

    [Fact]
    public void ServerOptions_WithCertificateContext_CarryContext_LeaveProtocolToThePlatform()
    {
        using var certificate = CreateTestCertificate();
        var context = SslStreamCertificateContext.Create(certificate,
            additionalCertificates: null, offline: true);

        var options = TlsConfig.ServerOptions(context);

        options.EnabledSslProtocols.Should().Be(SslProtocols.None);
        options.ServerCertificateContext.Should().BeSameAs(context);
        options.ClientCertificateRequired.Should().BeFalse();
    }

    [Fact]
    public void ClientOptions_LeaveProtocolToThePlatform_AndWireTheCallback()
    {
        RemoteCertificateValidationCallback callback = (_, _, _, _) => true;

        var options = TlsConfig.ClientOptions("play.hybrasyl.com", callback);

        options.EnabledSslProtocols.Should().Be(SslProtocols.None);
        options.TargetHost.Should().Be("play.hybrasyl.com");
        options.RemoteCertificateValidationCallback.Should().BeSameAs(callback);
    }

    [Fact]
    public void ClientOptions_NullCallback_MeansPlatformValidation()
    {
        var options = TlsConfig.ClientOptions("localhost");

        options.RemoteCertificateValidationCallback.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ClientOptions_RejectsAnEmptyOrWhitespaceTargetHost(string targetHost)
    {
        // An empty TargetHost removes hostname validation rather than relaxing it; .NET permits
        // it only with a callback that validates the subject, and this overload's is optional.
        var act = () => TlsConfig.ClientOptions(targetHost);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ClientOptions_ChecksRevocationByDefault_UnlikeDotNet()
    {
        new SslClientAuthenticationOptions().CertificateRevocationCheckMode
            .Should().Be(X509RevocationMode.NoCheck,
                "this is the platform default being deliberately overridden");

        TlsConfig.ClientOptions("play.hybrasyl.com").CertificateRevocationCheckMode
            .Should().Be(X509RevocationMode.Online,
                "on the public-CA path revocation is the only thing that retires a compromised " +
                "certificate before it expires");
    }

    [Fact]
    public void ClientOptions_AllowsRevocationCheckingToBeTurnedOffDeliberately()
    {
        // TOFU/self-signed and offline deployments: no responder to ask, so the check buys only a
        // doomed round trip.
        TlsConfig.ClientOptions("localhost", revocationMode: X509RevocationMode.NoCheck)
            .CertificateRevocationCheckMode.Should().Be(X509RevocationMode.NoCheck);
    }

    [Fact]
    public void ServerOptions_LeaveRevocationAlone_BecauseNoClientChainIsEverBuilt()
    {
        // Server-side this property governs the *client's* certificate. None is requested, so
        // there is no chain to check and setting it would imply a check with nothing to check.
        using var certificate = CreateTestCertificate();

        var options = TlsConfig.ServerOptions(certificate);

        options.ClientCertificateRequired.Should().BeFalse();
        options.CertificateRevocationCheckMode.Should().Be(X509RevocationMode.NoCheck);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var serverAct = () => TlsConfig.ServerOptions((X509Certificate2)null!);
        var contextAct = () => TlsConfig.ServerOptions((SslStreamCertificateContext)null!);
        var clientAct = () => TlsConfig.ClientOptions(null!);

        serverAct.Should().Throw<ArgumentNullException>();
        contextAct.Should().Throw<ArgumentNullException>();
        clientAct.Should().Throw<ArgumentNullException>();
    }
}
