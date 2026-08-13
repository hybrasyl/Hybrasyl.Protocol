using Hybrasyl.Protocol.Packets;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Packets;

public class HeartbeatTests
{
    [Fact]
    public void ClientEcho_ExactBodyLayout_IsBigEndianU64()
    {
        new ClientEcho(0x0123456789ABCDEF).ToBody().Should().Equal(
            0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    [InlineData(0x00000000FFFFFFFFUL)] // low half only - catches a swapped-halves bug
    [InlineData(0xFFFFFFFF00000000UL)] // high half only
    public void BothEchoes_RoundTripTokens(ulong token)
    {
        ClientEcho.Parse(new ClientEcho(token).ToBody()).Token.Should().Be(token);
        ServerEcho.Parse(new ServerEcho(token).ToBody()).Token.Should().Be(token);
    }

    [Fact]
    public void ClientEcho_RoundTripsThroughCodec_InBothDirections()
    {
        // One type, one opcode, registered in both tables: C->S is the probe and S->C is the
        // reply, so a decode must succeed on either side.
        var codec = new ExtensionCodec();
        var echo = new ClientEcho(0x1122334455667788);

        codec.TryDecodeClient(codec.EncodeClient(echo, Dialect.V1), out var probe, out _,
                Dialect.V1)
            .Should().BeTrue();
        probe.Should().Be(echo);

        codec.TryDecodeServer(codec.EncodeServer(echo, Dialect.V1), out var reply, out _,
                Dialect.V1)
            .Should().BeTrue();
        reply.Should().Be(echo);
    }

    [Fact]
    public void ServerEcho_RoundTripsThroughCodec_InBothDirections()
    {
        var codec = new ExtensionCodec();
        var echo = new ServerEcho(0x99AABBCCDDEEFF00);

        codec.TryDecodeServer(codec.EncodeServer(echo, Dialect.V1), out var probe, out _,
                Dialect.V1)
            .Should().BeTrue();
        probe.Should().Be(echo);

        codec.TryDecodeClient(codec.EncodeClient(echo, Dialect.V1), out var reply, out _,
                Dialect.V1)
            .Should().BeTrue();
        reply.Should().Be(echo);
    }

    [Fact]
    public void EachExchange_IsAnsweredAtTheNumberItWasSentOn()
    {
        // The allocation rule, asserted rather than described: a probe and its reply carry the
        // same opcode, and the two initiators carry different ones.
        new ClientEcho(0).Opcode.Should().Be(ExtensionOpcodes.ClientEcho);
        new ServerEcho(0).Opcode.Should().Be(ExtensionOpcodes.ServerEcho);
        ExtensionOpcodes.ClientEcho.Should().NotBe(ExtensionOpcodes.ServerEcho);
    }

    [Fact]
    public void Opcodes_AreTheFirstSystemBlockAllocations()
    {
        ExtensionOpcodes.ClientEcho.Should().Be((ushort)0x0100);
        ExtensionOpcodes.ServerEcho.Should().Be((ushort)0x0101);
    }
}
