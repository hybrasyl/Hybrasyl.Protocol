using System.Text;
using DALib.Networking.Packets.Server;
using Hybrasyl.Protocol.Negotiation;

namespace Hybrasyl.Protocol.Tests.Negotiation;

public class CapabilityMarkerTests
{
    private static byte[] RetailGreetingBody() => new AcceptConnectionPacket().ToBody();

    [Fact]
    public void ToBytes_HasExpectedLayout()
    {
        CapabilityMarker.Current.ToBytes()
            .Should().Equal(0x00, 0x48, 0x59, 0x42, 0x01, 0x00);
    }

    [Fact]
    public void BuildGreetingBody_AppendsMarkerAfterRetailGreeting()
    {
        var body = CapabilityMarker.Current.BuildGreetingBody();

        // [0x1B]"CONNECTED SERVER"[0x00]"HYB"[ver][flags]
        var expected = new byte[] { 0x1B }
            .Concat(Encoding.Latin1.GetBytes("CONNECTED SERVER"))
            .Concat(new byte[] { 0x00, 0x48, 0x59, 0x42, 0x01, 0x00 })
            .ToArray();

        body.Should().Equal(expected);
    }

    [Fact]
    public void TryRead_DetectsMarkerAndParsesFields()
    {
        var body = new CapabilityMarker(CapabilityMarker.CurrentVersion, CapabilityFlags.None)
            .BuildGreetingBody();

        var ok = CapabilityMarker.TryRead(body, out var marker);

        ok.Should().BeTrue();
        marker.Version.Should().Be(CapabilityMarker.CurrentVersion);
        marker.Flags.Should().Be(CapabilityFlags.None);
    }

    [Fact]
    public void TryRead_RoundTripsNonDefaultFields()
    {
        var original = new CapabilityMarker(0x07, (CapabilityFlags)0x03);
        var body = original.BuildGreetingBody();

        CapabilityMarker.TryRead(body, out var marker).Should().BeTrue();
        marker.Should().Be(original);
    }

    [Fact]
    public void TryRead_PlainRetailGreeting_ReturnsFalse()
    {
        CapabilityMarker.TryRead(RetailGreetingBody(), out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_EmptyBody_ReturnsFalse()
    {
        CapabilityMarker.TryRead(ReadOnlySpan<byte>.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRead_TruncatedMarker_ReturnsFalse()
    {
        // Magic present but version/flags cut off.
        var body = RetailGreetingBody().Concat(new byte[] { 0x00, 0x48, 0x59, 0x42, 0x01 }).ToArray();

        CapabilityMarker.TryRead(body, out _).Should().BeFalse();
    }
}
