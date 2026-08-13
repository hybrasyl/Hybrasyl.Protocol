using System;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Negotiation;

namespace Hybrasyl.Protocol.Tests.Negotiation;

public class DialectPolicyTests
{
    [Fact]
    public void Client_InRange_ResolvesToExtensionWithItsDialect()
    {
        var policy = new ClientDialectPolicy(Dialect.V1);
        var offer = new DialectOffer(0xB0, 0xB2);

        var resolution = policy.Resolve(offer);

        resolution.Mode.Should().Be(ConnectionMode.DialectOverTls);
        resolution.Dialect.Should().Be(Dialect.V1);
    }

    [Fact]
    public void Client_BelowFloor_ResolvesToRetailOverTls()
    {
        var policy = new ClientDialectPolicy(Dialect.V1); // 0xB0
        var offer = new DialectOffer(0xB1, 0xB2);

        var resolution = policy.Resolve(offer);

        resolution.Mode.Should().Be(ConnectionMode.RetailOverTls);
        resolution.Dialect.Should().BeNull();
    }

    [Fact]
    public void Client_AboveCeiling_ResolvesToRetailOverTls()
    {
        var policy = new ClientDialectPolicy((Dialect)0xB3);
        var offer = new DialectOffer(0xB0, 0xB2);

        var resolution = policy.Resolve(offer);

        resolution.Mode.Should().Be(ConnectionMode.RetailOverTls);
    }

    [Fact]
    public void Server_ToOffer_ReflectsRange()
    {
        var policy = ServerDialectPolicy.Create(Dialect.V1, (Dialect)0xB2);

        var offer = policy.ToOffer();

        offer.MinDialect.Should().Be((byte)0xB0);
        offer.MaxDialect.Should().Be((byte)0xB2);
        policy.Supports(Dialect.V1).Should().BeTrue();
        policy.Supports((Dialect)0xB3).Should().BeFalse();
    }

    [Fact]
    public void Server_Resolve_MatchesSupport()
    {
        var policy = ServerDialectPolicy.Create(Dialect.V1, (Dialect)0xB2);

        policy.Resolve(Dialect.V1).Mode.Should().Be(ConnectionMode.DialectOverTls);
        policy.Resolve((Dialect)0xB3).Mode.Should().Be(ConnectionMode.RetailOverTls);
    }

    [Fact]
    public void Server_Create_InvertedRange_Throws()
    {
        var act = () => ServerDialectPolicy.Create((Dialect)0xB2, Dialect.V1);

        act.Should().Throw<ArgumentException>().WithMessage("*exceeds ceiling*");
    }

    [Theory]
    [InlineData(0x00)] // unallocated low
    [InlineData(0xAA)] // retail's framing marker
    [InlineData(0xAF)] // the deliberate buffer below 0xB0
    [InlineData(0xFF)] // never allocated
    public void Server_Create_BoundOutsideTheDialectRange_Throws(byte bound)
    {
        // A Dialect is a cast away from any byte, so an ordering check alone would pass
        // (0x00, 0x10) as validated and land the failure on the peer's reader.
        var asFloor = () => ServerDialectPolicy.Create((Dialect)bound, (Dialect)0xFE);
        asFloor.Should().Throw<ArgumentException>().WithMessage("*not an allocatable dialect*");

        var asCeiling = () => ServerDialectPolicy.Create(Dialect.V1, (Dialect)bound);
        asCeiling.Should().Throw<ArgumentException>().WithMessage("*not an allocatable dialect*");
    }

    [Theory]
    [InlineData(0x00, 0x10)] // both outside the range
    [InlineData(0xAA, 0xB2)] // floor is retail's marker
    [InlineData(0xB0, 0xFF)] // ceiling is the never-allocated dialect
    public void Offer_ToBytes_RefusesWhatItsOwnReaderWouldReject(byte min, byte max)
    {
        // ToBytes must refuse what TryRead refuses, or a server can put a range on the wire
        // that every conforming peer rejects.
        var act = () => new DialectOffer(min, max).ToBytes();

        act.Should().Throw<InvalidDataException>().WithMessage("*out of range*");
    }

    [Fact]
    public void Offer_ToBytes_RefusesAnInvertedRange()
    {
        var act = () => new DialectOffer(0xB2, 0xB0).ToBytes();

        act.Should().Throw<InvalidDataException>().WithMessage("*exceeds ceiling*");
    }

    [Fact]
    public void Offer_RoundTripsThroughItsOwnReader()
    {
        // The positive control: validation must not refuse legitimate ranges.
        var bytes = new DialectOffer(0xB0, 0xFE).ToBytes();

        DialectOffer.TryRead(bytes, out var offer, out var consumed).Should().BeTrue();
        offer.Should().Be(new DialectOffer(0xB0, 0xFE));
        consumed.Should().Be(bytes.Length);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xAA)]
    [InlineData(0xFF)]
    public void Choice_ToBytes_RefusesADialectItsOwnReaderWouldReject(byte chosen)
    {
        var act = () => new DialectChoice(chosen, "test/1.0").ToBytes();

        act.Should().Throw<InvalidDataException>().WithMessage("*out of range*");
    }

    [Fact]
    public void Choice_RoundTripsThroughItsOwnReader()
    {
        var bytes = new DialectChoice(Dialect.V1, "brigid/0.1").ToBytes();

        DialectChoice.TryRead(bytes, out var choice, out _).Should().BeTrue();
        choice.Chosen.Should().Be(Dialect.V1);
        choice.ClientVersion.Should().Be("brigid/0.1");
    }

    [Fact]
    public void ClientChoice_AgreesWithServerResolution_InRange()
    {
        var server = ServerDialectPolicy.Create(Dialect.V1, Dialect.V1);
        var client = new ClientDialectPolicy(Dialect.V1);

        var clientResolution = client.Resolve(server.ToOffer());
        clientResolution.Mode.Should().Be(ConnectionMode.DialectOverTls);

        var serverResolution = server.Resolve(clientResolution.Dialect!.Value);
        serverResolution.Should().Be(clientResolution);
    }
}
