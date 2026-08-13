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
        consumed.Should().Be(NegotiationEnvelope.HeaderLength + DialectOffer.PayloadLength);
        read.Min.Should().Be(Dialect.V1);
    }

    [Fact]
    public void DialectOffer_ExactWireLayout_IsEnvelopeThenRange()
    {
        // [0xFF marker][u16 length = type + 2][0x00 DialectOffer][min][max]
        new DialectOffer(0xB0, 0xB2).ToBytes()
            .Should().Equal(0xFF, 0x00, 0x03, 0x00, 0xB0, 0xB2);
    }

    [Fact]
    public void DialectChoice_ExactWireLayout_IsEnvelopeThenPayload()
    {
        // [0xFF][u16 length = type + dialect + string8][0x01 DialectChoice][dialect][len]["ab"]
        new DialectChoice(Dialect.V1, "ab").ToBytes()
            .Should().Equal(0xFF, 0x00, 0x05, 0x01, 0xB0, 0x02, 0x61, 0x62);
    }

    [Fact]
    public void Negotiation_MessagesAreDistinguishedByType_NotByPosition()
    {
        // The point of the type byte: a reader handed the wrong message rejects it rather than
        // parsing whatever arrived as whatever it expected next.
        var choiceBytes = new DialectChoice(Dialect.V1, "v").ToBytes();

        var act = () => DialectOffer.TryRead(choiceBytes, out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*type 0x01*expected 0x00*");
    }

    [Fact]
    public void Negotiation_NonMarkerFirstByte_Throws()
    {
        var act = () => DialectOffer.TryRead(new byte[] { 0xAA, 0x00, 0x03, 0x00, 0xB0, 0xB0 },
            out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*starts 0xAA*expected the 0xFF*");
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
    public void DialectOffer_InvalidDialect_Throws()
    {
        var act = () => DialectOffer.TryRead(
            NegotiationEnvelope.Write(NegotiationMessageType.DialectOffer, [0xAA, 0xB0]),
            out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*out of range*");
    }

    [Fact]
    public void DialectOffer_FloorAboveCeiling_Throws()
    {
        var act = () => DialectOffer.TryRead(
            NegotiationEnvelope.Write(NegotiationMessageType.DialectOffer, [0xB2, 0xB0]),
            out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*floor*exceeds ceiling*");
    }

    [Fact]
    public void DialectOffer_PayloadNotExactlyTwoBytes_Throws()
    {
        // The envelope length and the message's own shape must agree; a longer payload is the
        // dangerous direction, since it would otherwise parse cleanly and discard the excess.
        var act = () => DialectOffer.TryRead(
            NegotiationEnvelope.Write(NegotiationMessageType.DialectOffer, [0xB0, 0xB2, 0xB4]),
            out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*payload is 3 bytes*expected exactly 2*");
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
    public void DialectChoice_InvalidDialect_Throws()
    {
        // dialect 0xAA, version length 0
        var act = () => DialectChoice.TryRead(
            NegotiationEnvelope.Write(NegotiationMessageType.DialectChoice, [0xAA, 0x00]),
            out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*out of range*");
    }

    [Fact]
    public void DialectChoice_EnvelopeAndString8LengthsDisagree_Throws()
    {
        // The string8 claims 4 version bytes but the envelope carries none: two fields describing
        // the same boundary, and a reader must not pick one and trust it.
        var act = () => DialectChoice.TryRead(
            NegotiationEnvelope.Write(NegotiationMessageType.DialectChoice, [0xB0, 0x04]),
            out _, out _);

        act.Should().Throw<InvalidDataException>().WithMessage("*implies 6*");
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
