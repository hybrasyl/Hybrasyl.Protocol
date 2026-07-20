using System;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>
///     Unit tests for the internal latest-wins resolution, exercised directly because there is
///     only one public <see cref="Dialect" /> today - the algorithm must be correct before a
///     second dialect signature exists to test it end-to-end.
/// </summary>
public class ExtensionDispatchTableTests
{
    private static ExtensionDecodeFn Marker(uint nonce) =>
        _ => DecodedPacket.FromExtension(new TestPingPacket { Nonce = nonce });

    [Fact]
    public void Resolve_ReturnsHighestIntroductionAtOrBelowSignature()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), "v1");
        table.Add(0x0100, 0xB1, Marker(0xB1), "v2");

        // At 0xB0 only v1 applies; at 0xB1 the newer shape wins.
        Nonce(table.Resolve(0xB0, 0x0100)).Should().Be(0xB0u);
        Nonce(table.Resolve(0xB1, 0x0100)).Should().Be(0xB1u);
        // Above the newest introduction, still the newest.
        Nonce(table.Resolve(0xB2, 0x0100)).Should().Be(0xB1u);
    }

    [Fact]
    public void Resolve_BelowEveryIntroduction_ReturnsNull()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), "v1");

        table.Resolve(0xAF, 0x0100).Should().BeNull();
    }

    [Fact]
    public void Resolve_UnknownOpcode_ReturnsNull()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), "v1");

        table.Resolve(0xB0, 0x0999).Should().BeNull();
    }

    [Fact]
    public void Add_DuplicateOpcodeAndSignature_Throws()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), "first");

        var act = () => table.Add(0x0100, 0xB0, Marker(0xB0), "second");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate*0x0100*0xB0*");
    }

    [Fact]
    public void Counts_ReflectOpcodesAndShapes()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), "v1");
        table.Add(0x0100, 0xB1, Marker(0xB1), "v2");
        table.Add(0x0101, 0xB0, Marker(0), "other");

        table.OpcodeCount.Should().Be(2);
        table.ShapeCount.Should().Be(3);
    }

    private static uint Nonce(ExtensionDecodeFn? fn)
    {
        fn.Should().NotBeNull();
        var decoded = fn!(ReadOnlySpan<byte>.Empty);

        return ((TestPingPacket)decoded.Extension!).Nonce;
    }
}
