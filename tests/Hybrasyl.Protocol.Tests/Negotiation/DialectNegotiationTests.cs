using System.IO;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Negotiation;

namespace Hybrasyl.Protocol.Tests.Negotiation;

public class DialectNegotiationTests
{
    [Fact]
    public void DialectOffer_RoundTrips()
    {
        var offer = new DialectOffer(Dialect.V1, Dialect.V1);

        DialectOffer.TryRead(offer.ToBytes(), out var read, out var consumed).Should().BeTrue();
        read.Should().Be(offer);
        consumed.Should().Be(2);
        read.Min.Should().Be(Dialect.V1);
    }

    [Fact]
    public void DialectOffer_ContainsResolvesRange()
    {
        var offer = new DialectOffer(0xB0, 0xB2);

        offer.Contains(Dialect.V1).Should().BeTrue();
        offer.Contains((byte)0xB2).Should().BeTrue();
        offer.Contains((byte)0xB3).Should().BeFalse();
        offer.Contains((byte)0xAF).Should().BeFalse();
    }

    [Fact]
    public void DialectOffer_PartialBuffer_ReturnsFalse()
    {
        DialectOffer.TryRead(new byte[] { 0xB0 }, out _, out var consumed).Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void DialectOffer_InvalidSignature_Throws()
    {
        var act = () => DialectOffer.TryRead(new byte[] { 0xAA, 0xB0 }, out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*out of range*");
    }

    [Fact]
    public void DialectOffer_FloorAboveCeiling_Throws()
    {
        var act = () => DialectOffer.TryRead(new byte[] { 0xB2, 0xB0 }, out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*floor*exceeds ceiling*");
    }

    [Fact]
    public void DialectChoice_RoundTrips()
    {
        var choice = new DialectChoice(Dialect.V1, "brigid/0.4.0");

        DialectChoice.TryRead(choice.ToBytes(), out var read, out var consumed).Should().BeTrue();
        read.Should().Be(choice);
        read.Chosen.Should().Be(Dialect.V1);
        consumed.Should().Be(choice.ToBytes().Length);
    }

    [Fact]
    public void DialectChoice_EmptyVersion_RoundTrips()
    {
        var choice = new DialectChoice(Dialect.V1, "");

        DialectChoice.TryRead(choice.ToBytes(), out var read, out _).Should().BeTrue();
        read.ClientVersion.Should().BeEmpty();
    }

    [Fact]
    public void DialectChoice_PartialBuffer_ReturnsFalse()
    {
        var full = new DialectChoice(Dialect.V1, "abc").ToBytes();

        DialectChoice.TryRead(full.AsMemory(0, full.Length - 1), out _, out var consumed)
            .Should().BeFalse();
        consumed.Should().Be(0);
    }

    [Fact]
    public void DialectChoice_InvalidSignature_Throws()
    {
        // signature 0xAA, version length 0
        var act = () => DialectChoice.TryRead(new byte[] { 0xAA, 0x00 }, out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*out of range*");
    }

    [Fact]
    public void Negotiation_DrainsBothMessagesFromOneStream()
    {
        var offer = new DialectOffer(Dialect.V1, Dialect.V1).ToBytes();
        var choice = new DialectChoice(Dialect.V1, "v").ToBytes();
        var stream = new byte[offer.Length + choice.Length];
        offer.CopyTo(stream, 0);
        choice.CopyTo(stream, offer.Length);

        var buffer = stream.AsMemory();

        DialectOffer.TryRead(buffer, out _, out var c1).Should().BeTrue();
        buffer = buffer[c1..];
        DialectChoice.TryRead(buffer, out var readChoice, out var c2).Should().BeTrue();
        readChoice.ClientVersion.Should().Be("v");

        buffer = buffer[c2..];
        buffer.Length.Should().Be(0);
    }
}
