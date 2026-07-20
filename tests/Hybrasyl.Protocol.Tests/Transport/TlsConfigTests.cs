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
    public void ServerOptions_PinTls13_CarryCert_RequireNoClientCert()
    {
        using var certificate = CreateTestCertificate();

        var options = TlsConfig.ServerOptions(certificate);

        options.EnabledSslProtocols.Should().Be(SslProtocols.Tls13);
        options.ServerCertificate.Should().BeSameAs(certificate);
        options.ClientCertificateRequired.Should().BeFalse();
    }

    [Fact]
    public void ClientOptions_PinTls13_AndWireTheCallback()
    {
        RemoteCertificateValidationCallback callback = (_, _, _, _) => true;

        var options = TlsConfig.ClientOptions("play.hybrasyl.com", callback);

        options.EnabledSslProtocols.Should().Be(SslProtocols.Tls13);
        options.TargetHost.Should().Be("play.hybrasyl.com");
        options.RemoteCertificateValidationCallback.Should().BeSameAs(callback);
    }

    [Fact]
    public void ClientOptions_NullCallback_MeansPlatformValidation()
    {
        var options = TlsConfig.ClientOptions("localhost");

        options.RemoteCertificateValidationCallback.Should().BeNull();
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var serverAct = () => TlsConfig.ServerOptions(null!);
        var clientAct = () => TlsConfig.ClientOptions(null!);

        serverAct.Should().Throw<ArgumentNullException>();
        clientAct.Should().Throw<ArgumentNullException>();
    }
}
