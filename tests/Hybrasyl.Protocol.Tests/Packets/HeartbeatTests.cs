using Hybrasyl.Protocol.Packets;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Packets;

public class HeartbeatTests
{
    [Fact]
    public void Ping_ExactBodyLayout_IsBigEndianU64()
    {
        new Ping(0x0123456789ABCDEF).ToBody().Should().Equal(
            0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0x00000000FFFFFFFFUL)] // low half only - catches a swapped-halves bug
    [InlineData(0xFFFFFFFF00000000UL)] // high half only
    public void PingAndPong_RoundTripTokens(ulong token)
    {
        Ping.Parse(new Ping(token).ToBody()).Token.Should().Be(token);
        Pong.Parse(new Pong(token).ToBody()).Token.Should().Be(token);
    }

    [Fact]
    public void PongFor_EchoesThePingToken()
    {
        Pong.For(new Ping(0xCAFE)).Token.Should().Be(0xCAFEUL);
    }

    [Fact]
    public void Ping_RoundTripsThroughCodec_InBothDirections()
    {
        // The symmetric-exchange case: one type, same opcode, registered in both tables.
        var codec = new ExtensionCodec();
        var ping = new Ping(0x1122334455667788);

        codec.TryDecodeClient(codec.EncodeClient(ping, Dialect.V1), out var fromClient, out _)
            .Should().BeTrue();
        fromClient.Should().Be(ping);

        codec.TryDecodeServer(codec.EncodeServer(ping, Dialect.V1), out var fromServer, out _)
            .Should().BeTrue();
        fromServer.Should().Be(ping);
    }

    [Fact]
    public void Pong_RoundTripsThroughCodec_InBothDirections()
    {
        var codec = new ExtensionCodec();
        var pong = new Pong(0x99AABBCCDDEEFF00);

        codec.TryDecodeClient(codec.EncodeClient(pong, Dialect.V1), out var fromClient, out _)
            .Should().BeTrue();
        fromClient.Should().Be(pong);

        codec.TryDecodeServer(codec.EncodeServer(pong, Dialect.V1), out var fromServer, out _)
            .Should().BeTrue();
        fromServer.Should().Be(pong);
    }

    [Fact]
    public void Opcodes_AreTheFirstSystemBlockAllocations()
    {
        ExtensionOpcodes.Ping.Should().Be((ushort)0x0100);
        ExtensionOpcodes.Pong.Should().Be((ushort)0x0101);
        new Ping(0).Opcode.Should().Be(ExtensionOpcodes.Ping);
        new Pong(0).Opcode.Should().Be(ExtensionOpcodes.Pong);
    }
}
