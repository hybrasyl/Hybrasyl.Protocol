using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Hybrasyl.Protocol.Transport;

namespace Hybrasyl.Protocol.Tests.Transport;

public class TlsChannelTests
{
    private static readonly TimeSpan HandshakeBudget = TimeSpan.FromSeconds(10);

    private static X509Certificate2 CreateTestCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=sreang-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    /// <summary>A connected loopback pair, so the handshake under test is a real one.</summary>
    private static async Task<(NetworkStream Client, NetworkStream Server)> ConnectedPairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback,
                ((IPEndPoint)listener.LocalEndpoint).Port);
            var server = await acceptTask;

            return (client.GetStream(), server.GetStream());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Upgrade_NegotiatesTls13_WithoutRequestingIt()
    {
        // The positive control for the whole change. Leaving EnabledSslProtocols at None is only
        // correct if the platform then *chooses* 1.3 - otherwise the options are portable and the
        // channel is unusable everywhere instead of just on macOS. Asserting the constant is set
        // proves nothing; this asserts what a real handshake produced.
        using var cts = new CancellationTokenSource(HandshakeBudget);
        using var certificate = CreateTestCertificate();
        var (clientStream, serverStream) = await ConnectedPairAsync();

        await using var _ = clientStream;
        await using var __ = serverStream;

        var serverTask = TlsChannel.UpgradeAsServerAsync(
            serverStream, TlsConfig.ServerOptions(certificate), cancellationToken: cts.Token);
        var clientTask = TlsChannel.UpgradeAsClientAsync(
            clientStream,
            TlsConfig.ClientOptions("sreang-test", (_, _, _, _) => true,
                X509RevocationMode.NoCheck),
            cancellationToken: cts.Token);

        await using var serverSsl = await serverTask;
        await using var clientSsl = await clientTask;

        clientSsl.SslProtocol.Should().Be(SslProtocols.Tls13);
        serverSsl.SslProtocol.Should().Be(SslProtocols.Tls13);
        clientSsl.IsEncrypted.Should().BeTrue();
        clientSsl.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task Upgrade_CarriesApplicationDataBothWays()
    {
        // The upgrade must yield a usable duplex stream, not merely an authenticated one.
        using var cts = new CancellationTokenSource(HandshakeBudget);
        using var certificate = CreateTestCertificate();
        var (clientStream, serverStream) = await ConnectedPairAsync();

        await using var _ = clientStream;
        await using var __ = serverStream;

        var serverTask = TlsChannel.UpgradeAsServerAsync(
            serverStream, TlsConfig.ServerOptions(certificate), cancellationToken: cts.Token);
        var clientTask = TlsChannel.UpgradeAsClientAsync(
            clientStream,
            TlsConfig.ClientOptions("sreang-test", (_, _, _, _) => true,
                X509RevocationMode.NoCheck),
            cancellationToken: cts.Token);

        await using var serverSsl = await serverTask;
        await using var clientSsl = await clientTask;

        var sent = new byte[] { 0xFF, 0x00, 0x03, 0x00, 0xB0, 0xB0 };
        await serverSsl.WriteAsync(sent, cts.Token);
        await serverSsl.FlushAsync(cts.Token);

        var received = new byte[sent.Length];
        await clientSsl.ReadExactlyAsync(received, cts.Token);

        received.Should().Equal(sent);
    }

    [Fact]
    public async Task VerifyNegotiatedProtocol_RejectsARealTls12Handshake()
    {
        // The branch the whole change exists for, exercised against an actual 1.2 handshake rather
        // than a stand-in: a platform that negotiates something older must be caught here, since
        // nothing upstream can catch it once the options stop pinning.
        using var cts = new CancellationTokenSource(HandshakeBudget);
        using var certificate = CreateTestCertificate();
        var (clientStream, serverStream) = await ConnectedPairAsync();

        await using var _ = clientStream;
        await using var __ = serverStream;

        var serverOptions = TlsConfig.ServerOptions(certificate);
        serverOptions.EnabledSslProtocols = SslProtocols.Tls12;
        var clientOptions = TlsConfig.ClientOptions("sreang-test", (_, _, _, _) => true,
            X509RevocationMode.NoCheck);
        clientOptions.EnabledSslProtocols = SslProtocols.Tls12;

        await using var serverSsl = new SslStream(serverStream, leaveInnerStreamOpen: true);
        await using var clientSsl = new SslStream(clientStream, leaveInnerStreamOpen: true);

        var serverTask = serverSsl.AuthenticateAsServerAsync(serverOptions, cts.Token);
        await clientSsl.AuthenticateAsClientAsync(clientOptions, cts.Token);
        await serverTask;

        clientSsl.SslProtocol.Should().Be(SslProtocols.Tls12, "the positive control: 1.2 really was negotiated");

        var act = () => TlsChannel.VerifyNegotiatedProtocol(clientSsl);

        act.Should().Throw<AuthenticationException>().WithMessage("*requires Tls13*");
    }

    [Fact]
    public void VerifyNegotiatedProtocol_RejectsAStreamThatHasNotHandshakedYet()
    {
        // SslProtocol throws before authentication, so without this guard a check called too early
        // fails as an InvalidOperationException that says nothing about the transport requirement.
        using var inner = new MemoryStream();
        using var ssl = new SslStream(inner);

        var act = () => TlsChannel.VerifyNegotiatedProtocol(ssl);

        act.Should().Throw<AuthenticationException>().WithMessage("*before the handshake*");
    }

    [Fact]
    public void VerifyNegotiatedProtocol_RejectsNull()
    {
        var act = () => TlsChannel.VerifyNegotiatedProtocol(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Upgrade_RejectsNullArguments()
    {
        using var certificate = CreateTestCertificate();
        var options = TlsConfig.ServerOptions(certificate);

        var nullStream = async () => await TlsChannel.UpgradeAsServerAsync(null!, options);
        var nullOptions = async () =>
            await TlsChannel.UpgradeAsClientAsync(new MemoryStream(), null!);

        await nullStream.Should().ThrowAsync<ArgumentNullException>();
        await nullOptions.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Upgrade_HonoursItsCancellationToken()
    {
        // The handshake deadline: a peer that connects and then says nothing must not block
        // forever. Nothing is ever sent from the other end here.
        var (clientStream, serverStream) = await ConnectedPairAsync();

        await using var _ = clientStream;
        await using var __ = serverStream;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var act = async () => await TlsChannel.UpgradeAsClientAsync(
            clientStream,
            TlsConfig.ClientOptions("sreang-test", (_, _, _, _) => true,
                X509RevocationMode.NoCheck),
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<Exception>();
    }
}
