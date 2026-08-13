using System;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>
///     Unit tests for the internal latest-wins resolution, exercised directly because there is
///     only one public <see cref="Dialect" /> today - the algorithm must be correct before a
///     second dialect exists to test it end-to-end.
/// </summary>
public class ExtensionDispatchTableTests
{
    // Three unrelated registered types, standing in for one opcode's successive shapes. Their
    // identity is what matters here, not their bodies.
    private static readonly Type ShapeV1 = typeof(TestServerNoncePacket);
    private static readonly Type ShapeV2 = typeof(TestClientBytePacket);
    private static readonly Type Other = typeof(TestUpgradedMapInfoPacket);

    private static ExtensionDecodeFn Marker(uint nonce) =>
        _ => new TestServerNoncePacket { Nonce = nonce };

    [Fact]
    public void Resolve_ReturnsHighestIntroductionAtOrBelowDialect()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), ShapeV1);
        table.Add(0x0100, 0xB1, Marker(0xB1), ShapeV2);

        Nonce(table.Resolve(0xB0, 0x0100)).Should().Be(0xB0u);
        Nonce(table.Resolve(0xB1, 0x0100)).Should().Be(0xB1u);
        Nonce(table.Resolve(0xB2, 0x0100)).Should().Be(0xB1u);
    }

    [Fact]
    public void ResolveType_TracksResolveExactly()
    {
        // The encode-side check is only sound if the type it compares against is the type the
        // decode side would actually construct. Pinned together so the two cannot drift.
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), ShapeV1);
        table.Add(0x0100, 0xB1, Marker(0xB1), ShapeV2);

        table.ResolveType(0xB0, 0x0100).Should().Be(ShapeV1);
        table.ResolveType(0xB1, 0x0100).Should().Be(ShapeV2);
        table.ResolveType(0xB2, 0x0100).Should().Be(ShapeV2);
        table.ResolveType(0xAF, 0x0100).Should().BeNull();
        table.ResolveType(0xB0, 0x0999).Should().BeNull();
    }

    [Fact]
    public void Resolve_BelowEveryIntroduction_ReturnsNull()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), ShapeV1);

        table.Resolve(0xAF, 0x0100).Should().BeNull();
    }

    [Fact]
    public void Resolve_UnknownOpcode_ReturnsNull()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), ShapeV1);

        table.Resolve(0xB0, 0x0999).Should().BeNull();
    }

    [Fact]
    public void Add_DuplicateOpcodeAndDialect_Throws()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), ShapeV1);

        var act = () => table.Add(0x0100, 0xB0, Marker(0xB0), ShapeV2);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate*0x0100*0xB0*");
    }

    [Fact]
    public void Counts_ReflectOpcodesAndShapes()
    {
        var table = new ExtensionDispatchTable("test");
        table.Add(0x0100, 0xB0, Marker(0xB0), ShapeV1);
        table.Add(0x0100, 0xB1, Marker(0xB1), ShapeV2);
        table.Add(0x0101, 0xB0, Marker(0), Other);

        table.OpcodeCount.Should().Be(2);
        table.ShapeCount.Should().Be(3);
    }

    private static uint Nonce(ExtensionDecodeFn? fn)
    {
        fn.Should().NotBeNull();
        var decoded = fn!(ReadOnlySpan<byte>.Empty);

        return ((TestServerNoncePacket)decoded).Nonce;
    }
}
