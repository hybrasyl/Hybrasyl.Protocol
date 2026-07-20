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

        resolution.Mode.Should().Be(ConnectionMode.ExtensionOverTls);
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

        offer.MinSignature.Should().Be((byte)0xB0);
        offer.MaxSignature.Should().Be((byte)0xB2);
        policy.Supports(Dialect.V1).Should().BeTrue();
        policy.Supports((Dialect)0xB3).Should().BeFalse();
    }

    [Fact]
    public void Server_Resolve_MatchesSupport()
    {
        var policy = ServerDialectPolicy.Create(Dialect.V1, (Dialect)0xB2);

        policy.Resolve(Dialect.V1).Mode.Should().Be(ConnectionMode.ExtensionOverTls);
        policy.Resolve((Dialect)0xB3).Mode.Should().Be(ConnectionMode.RetailOverTls);
    }

    [Fact]
    public void Server_Create_InvertedRange_Throws()
    {
        var act = () => ServerDialectPolicy.Create((Dialect)0xB2, Dialect.V1);

        act.Should().Throw<ArgumentException>().WithMessage("*exceeds ceiling*");
    }

    [Fact]
    public void ClientChoice_AgreesWithServerResolution_InRange()
    {
        // End-to-end: server advertises, client resolves, server resolves the client's dialect —
        // both land on the same extension dialect.
        var server = ServerDialectPolicy.Create(Dialect.V1, Dialect.V1);
        var client = new ClientDialectPolicy(Dialect.V1);

        var clientResolution = client.Resolve(server.ToOffer());
        clientResolution.Mode.Should().Be(ConnectionMode.ExtensionOverTls);

        var serverResolution = server.Resolve(clientResolution.Dialect!.Value);
        serverResolution.Should().Be(clientResolution);
    }
}
