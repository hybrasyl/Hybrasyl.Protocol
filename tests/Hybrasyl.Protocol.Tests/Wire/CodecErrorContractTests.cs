using System;
using System.IO;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Framing;
using Hybrasyl.Protocol.Packets;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>
///     The codec's error contract on untrusted input: a malformed body is a protocol fault and
///     must arrive as <see cref="InvalidDataException" />, whatever the parser threw underneath.
/// </summary>
public class CodecErrorContractTests
{
    [Fact]
    public void AParserThatThrowsItsReadersException_IsNormalisedToInvalidData()
    {
        // TestServerNoncePacket reads a u32 with no length check of its own, so a short body
        // reaches DALib's PacketReader and it throws InvalidOperationException. Left unnormalised
        // that escapes the codec, and a consumer catching InvalidDataException to kill the
        // connection would miss it entirely.
        var codec = TestCodec.WithTestPackets();
        var wire = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0200, new byte[2]);

        var act = () => codec.TryDecodeServer(wire, out _, out _, Dialect.V1);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Malformed body*0x0200*")
            .And.InnerException.Should().BeOfType<InvalidOperationException>(
                "the original parser failure is preserved rather than discarded");
    }

    [Fact]
    public void AParserThatChecksItsOwnLength_PassesItsInvalidDataThrough_Unwrapped()
    {
        // ClientEcho validates its body length itself, so it already speaks the codec's error language.
        // That exception must reach the caller as-is rather than being re-wrapped into a vaguer
        // one - the specific message is the useful half.
        var wire = ExtensionFrameCodec.WriteFrame(Dialect.V1, ExtensionOpcodes.ClientEcho, new byte[3]);

        var act = () => new ExtensionCodec().TryDecodeClient(wire, out _, out _, Dialect.V1);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*ClientEcho body is 3 bytes; expected exactly 8*")
            .And.InnerException.Should().BeNull();
    }

    [Fact]
    public void OverlongBody_IsRejectedRatherThanSilentlyTruncated()
    {
        // The dangerous direction: 12 bytes parses cleanly if Parse just reads its 8 and stops,
        // so a framing bug or a version mismatch would present as a valid ClientEcho.
        var wire = ExtensionFrameCodec.WriteFrame(Dialect.V1, ExtensionOpcodes.ClientEcho,
            new byte[ClientEcho.BodyLength + 4]);

        var act = () => new ExtensionCodec().TryDecodeClient(wire, out _, out _, Dialect.V1);

        act.Should().Throw<InvalidDataException>().WithMessage("*expected exactly 8*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(64)]
    public void ClientEcho_RejectsAnyBodyThatIsNotExactlyEightBytes(int length)
    {
        var act = () => ClientEcho.Parse(new byte[length]);

        act.Should().Throw<InvalidDataException>().WithMessage("*expected exactly 8*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void ServerEcho_RejectsAnyBodyThatIsNotExactlyEightBytes(int length)
    {
        var act = () => ServerEcho.Parse(new byte[length]);

        act.Should().Throw<InvalidDataException>().WithMessage("*expected exactly 8*");
    }

    [Fact]
    public void ExactlyEightBytes_StillParses()
    {
        // Positive control for the four refusals above.
        ClientEcho.Parse(new byte[ClientEcho.BodyLength]).Token.Should().Be(0UL);
        ServerEcho.Parse(new byte[ServerEcho.BodyLength]).Token.Should().Be(0UL);

        var codec = new ExtensionCodec();
        var wire = codec.EncodeClient(new ClientEcho(0x1122334455667788), Dialect.V1);

        codec.TryDecodeClient(wire, out var packet, out _, Dialect.V1).Should().BeTrue();
        packet.Should().Be(new ClientEcho(0x1122334455667788));
    }
}
