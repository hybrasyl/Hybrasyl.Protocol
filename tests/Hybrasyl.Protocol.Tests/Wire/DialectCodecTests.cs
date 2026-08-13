using System;
using System.IO;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Framing;
using Hybrasyl.Protocol.Negotiation;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>
///     Tests for the connection-bound codec: the point of it is that the dialect is not a
///     per-call argument any caller can omit or get wrong, so most of these assert what the API
///     makes <em>impossible</em> rather than what it computes.
/// </summary>
public class DialectCodecTests
{
    private static DialectCodec BoundToV1() =>
        TestCodec.WithTestPackets().ForConnection(DialectResolution.Engaged(Dialect.V1));

    [Fact]
    public void ForConnection_RoundTripsWithoutNamingTheDialectAtAnyCallSite()
    {
        var channel = BoundToV1();

        var wire = channel.EncodeServer(new TestServerNoncePacket { Nonce = 0xDEADBEEF });

        channel.TryDecodeServer(wire, out var packet, out var consumed).Should().BeTrue();
        consumed.Should().Be(wire.Length);
        packet.Should().BeOfType<TestServerNoncePacket>().Which.Nonce.Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void ForConnection_StampsItsOwnDialect()
    {
        var channel = TestCodec.WithTestPackets()
            .ForConnection(DialectResolution.Engaged((Dialect)0xB1));

        var wire = channel.EncodeServer(new TestShapeReplacedAtB1 { Widened = 1 });

        // Byte 4 is the dialect, after the u32-BE length.
        wire[ExtensionFrame.LengthFieldLength].Should().Be(0xB1);
        channel.Dialect.Should().Be((Dialect)0xB1);
    }

    [Fact]
    public void Decode_FrameStampedWithAnotherDialect_IsRejected()
    {
        // There is no dialect argument to omit at the bound API, so a foreign stamp is refused
        // without the caller having to remember anything.
        var channel = BoundToV1();
        var foreign = ExtensionFrameCodec.WriteFrame((Dialect)0xB1, 0x0220,
            new TestShapeReplacedAtB1 { Widened = 1 }.ToBody());

        var act = () => channel.TryDecodeServer(foreign, out _, out _);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*does not match the negotiated dialect*");
    }

    [Fact]
    public void Encode_ShapeForeignToTheBoundDialect_IsRejected()
    {
        var channel = BoundToV1();

        var act = () => channel.EncodeServer(new TestShapeReplacedAtB1 { Widened = 1 });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*resolves to*TestShapeIntroducedAtV1*");
    }

    [Theory]
    [InlineData(ConnectionMode.RetailOverTls)]
    [InlineData(ConnectionMode.PlaintextRetail)]
    public void ForConnection_WithoutAnEngagedDialect_Throws(ConnectionMode mode)
    {
        // Retail modes carry 0xAA frames on DALib's codec. Binding one here would produce a
        // codec with no dialect to stamp, so it is refused at construction rather than at first
        // send.
        var resolution = mode == ConnectionMode.RetailOverTls
            ? DialectResolution.RetailOverTls
            : DialectResolution.PlaintextRetail;

        var act = () => TestCodec.WithTestPackets().ForConnection(resolution);

        act.Should().Throw<ArgumentException>().WithMessage("*engaged a dialect*");
    }

    [Fact]
    public void ForConnection_WithARetailModeCarryingADialect_Throws()
    {
        // DialectResolution is a public record struct, so a caller can build one its factories
        // never produce: a retail mode that nonetheless names a dialect. Only the mode check
        // refuses this - the factory-built retail resolutions carry a null dialect and are
        // caught by the other half of the condition, so without this case the mode check is
        // dead weight nothing exercises.
        var inconsistent = new DialectResolution(ConnectionMode.RetailOverTls, Dialect.V1);

        var act = () => TestCodec.WithTestPackets().ForConnection(inconsistent);

        act.Should().Throw<ArgumentException>().WithMessage("*engaged a dialect*");
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xAA)] // retail's own framing marker - the one that would corrupt routing
    [InlineData(0xAF)]
    [InlineData(0xFF)]
    public void Engaged_WithAnUnallocatableDialect_Throws(byte dialect)
    {
        // Dialect is a byte-backed enum, so an engaged resolution naming a dialect no
        // negotiation could produce is a cast away. Refused where the impossible state would
        // first be representable.
        var act = () => DialectResolution.Engaged((Dialect)dialect);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not an allocatable dialect*");
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xAA)]
    [InlineData(0xFF)]
    public void ForConnection_WithAnEngagedModeNamingABadDialect_Throws(byte dialect)
    {
        // DialectResolution's primary constructor is public, so this bypasses Engaged's guard
        // entirely. The facade re-checks rather than trusting its input: a codec bound to 0xAA
        // would stamp retail's marker into the dialect field of every frame it wrote.
        var smuggled = new DialectResolution(ConnectionMode.DialectOverTls, (Dialect)dialect);

        var act = () => TestCodec.WithTestPackets().ForConnection(smuggled);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not an allocatable dialect*");
    }

    [Fact]
    public void MaxFrameSize_AppliesToBothDirections()
    {
        // A cap given once at construction must reach the writer and the reader alike - the
        // total-wire-size symmetry.
        var channel = TestCodec.WithTestPackets()
            .ForConnection(DialectResolution.Engaged(Dialect.V1), ExtensionFrame.HeaderLength + 1);

        channel.MaxFrameSize.Should().Be(ExtensionFrame.HeaderLength + 1);

        var encode = () => channel.EncodeServer(new TestServerNoncePacket { Nonce = 0 });
        encode.Should().Throw<InvalidDataException>().WithMessage("*exceeds MaxFrameSize*");

        // Four body bytes: writable at the default cap, over this channel's.
        var oversized = ExtensionFrameCodec.WriteFrame(Dialect.V1, 0x0200, new byte[4]);
        var decode = () => channel.TryDecodeServer(oversized, out _, out _);
        decode.Should().Throw<InvalidDataException>().WithMessage("*exceeds MaxFrameSize*");
    }

    [Fact]
    public void Constructor_RejectsNonPositiveMaxFrameSize()
    {
        var act = () => TestCodec.WithTestPackets()
            .ForConnection(DialectResolution.Engaged(Dialect.V1), 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
