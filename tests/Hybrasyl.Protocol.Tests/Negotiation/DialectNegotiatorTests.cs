using System;
using System.IO;
using System.Threading.Tasks;
using Hybrasyl.Protocol.Negotiation;

namespace Hybrasyl.Protocol.Tests.Negotiation;

public class DialectNegotiatorTests
{
    private static readonly ServerDialectPolicy ServerV1 =
        ServerDialectPolicy.Create(Dialect.V1, Dialect.V1);

    [Fact]
    public async Task Server_WritesOffer_AndEngagesInRangeChoice()
    {
        var choice = new DialectChoice(Dialect.V1, "brigid/0.4.0");
        var stream = new ScriptedStream(choice.ToBytes());

        var result = await DialectNegotiator.NegotiateAsServerAsync(stream, ServerV1);

        stream.Written.Should().Equal(ServerV1.ToOffer().ToBytes());
        result.Resolution.Should().Be(DialectResolution.Engaged(Dialect.V1));
        result.Choice.ClientVersion.Should().Be("brigid/0.4.0");
    }

    [Fact]
    public async Task Server_OutOfRangeChoice_DerivesRetailOverTls_AndKeepsVersion()
    {
        // A future client speaking 0xB5 against a V1-only server: valid choice, no engagement.
        var choice = new DialectChoice(0xB5, "brigid/9.9.9");
        var stream = new ScriptedStream(choice.ToBytes());

        var result = await DialectNegotiator.NegotiateAsServerAsync(stream, ServerV1);

        result.Resolution.Should().Be(DialectResolution.RetailOverTls);
        result.Choice.ClientVersion.Should().Be("brigid/9.9.9");
    }

    [Fact]
    public async Task Server_DoesNotReadPastTheChoice()
    {
        // A client may pipeline frames right after its choice; those bytes must stay unread.
        var choice = new DialectChoice(Dialect.V1, "brigid/0.4.0");
        var pipelined = new byte[] { 0x00, 0x00, 0x00, 0x04, 0xB0, 0x01, 0x00, 0x00 };
        var stream = new ScriptedStream([.. choice.ToBytes(), .. pipelined]);

        await DialectNegotiator.NegotiateAsServerAsync(stream, ServerV1);

        stream.UnreadCount.Should().Be(pipelined.Length);
    }

    [Fact]
    public async Task Server_RetailSignatureChoice_Throws()
    {
        // 0xAA is not a dialect and never appears in a choice - retail-over-TLS is derived, not signaled.
        var stream = new ScriptedStream([0xAA, 0x01, (byte)'x']);

        var act = () => DialectNegotiator.NegotiateAsServerAsync(stream, ServerV1);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*out of range*");
    }

    [Fact]
    public async Task Client_InRange_EngagesAndSendsItsChoice()
    {
        var stream = new ScriptedStream(new DialectOffer(0xB0, 0xB2).ToBytes());
        var policy = new ClientDialectPolicy(Dialect.V1);

        var result = await DialectNegotiator.NegotiateAsClientAsync(stream, policy, "brigid/0.4.0");

        result.Resolution.Should().Be(DialectResolution.Engaged(Dialect.V1));
        result.Offer.Should().Be(new DialectOffer(0xB0, 0xB2));
        stream.Written.Should().Equal(new DialectChoice(Dialect.V1, "brigid/0.4.0").ToBytes());
    }

    [Fact]
    public async Task Client_BelowFloor_StillSendsRealDialect_AndDerivesRetailOverTls()
    {
        // Server retired V1: the client still sends its real dialect; both sides derive the mode.
        var stream = new ScriptedStream(new DialectOffer(0xB1, 0xB2).ToBytes());
        var policy = new ClientDialectPolicy(Dialect.V1);

        var result = await DialectNegotiator.NegotiateAsClientAsync(stream, policy, "brigid/0.4.0");

        result.Resolution.Should().Be(DialectResolution.RetailOverTls);
        stream.Written.Should().Equal(new DialectChoice(Dialect.V1, "brigid/0.4.0").ToBytes());
    }

    [Theory]
    [InlineData(0xB0, 0xB0)] // in range - engaged
    [InlineData(0xB1, 0xB2)] // below floor - retail-over-TLS
    public async Task BothSides_DeriveTheSameResolution(byte floor, byte ceiling)
    {
        var serverPolicy = ServerDialectPolicy.Create((Dialect)floor, (Dialect)ceiling);

        var clientStream = new ScriptedStream(serverPolicy.ToOffer().ToBytes());
        var client = await DialectNegotiator.NegotiateAsClientAsync(
            clientStream, new ClientDialectPolicy(Dialect.V1), "brigid/0.4.0");

        var serverStream = new ScriptedStream(clientStream.Written);
        var server = await DialectNegotiator.NegotiateAsServerAsync(serverStream, serverPolicy);

        server.Resolution.Should().Be(client.Resolution);
    }

    [Fact]
    public async Task Negotiation_SurvivesSingleByteReads()
    {
        var choice = new DialectChoice(Dialect.V1, "brigid/0.4.0");
        var serverStream = new ScriptedStream(choice.ToBytes(), chunkSize: 1);
        var clientStream = new ScriptedStream(ServerV1.ToOffer().ToBytes(), chunkSize: 1);

        var server = await DialectNegotiator.NegotiateAsServerAsync(serverStream, ServerV1);
        var client = await DialectNegotiator.NegotiateAsClientAsync(
            clientStream, new ClientDialectPolicy(Dialect.V1), "brigid/0.4.0");

        server.Resolution.Should().Be(DialectResolution.Engaged(Dialect.V1));
        client.Resolution.Should().Be(DialectResolution.Engaged(Dialect.V1));
    }

    [Fact]
    public async Task PrematureClose_ThrowsEndOfStream()
    {
        var serverAct = () => DialectNegotiator.NegotiateAsServerAsync(
            new ScriptedStream([0xB0]), ServerV1); // signature but no length byte
        var clientAct = () => DialectNegotiator.NegotiateAsClientAsync(
            new ScriptedStream([0xB0]), new ClientDialectPolicy(Dialect.V1), "v");

        await serverAct.Should().ThrowAsync<EndOfStreamException>();
        await clientAct.Should().ThrowAsync<EndOfStreamException>();
    }

    /// <summary>
    ///     A half-duplex test stream: serves <paramref name="input" /> to reads (at most
    ///     <paramref name="chunkSize" /> bytes at a time, to exercise partial-read loops) and
    ///     captures writes.
    /// </summary>
    private sealed class ScriptedStream(byte[] input, int chunkSize = int.MaxValue) : Stream
    {
        private readonly MemoryStream _written = new();
        private int _position;

        public byte[] Written => _written.ToArray();
        public int UnreadCount => input.Length - _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, chunkSize), input.Length - _position);
            Array.Copy(input, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            _written.Write(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
