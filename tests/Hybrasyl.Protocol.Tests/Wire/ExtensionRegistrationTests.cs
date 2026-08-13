using System;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Wire;

namespace Hybrasyl.Protocol.Tests.Wire;

/// <summary>
///     Registration-metadata validation. These are construction-time invariants: if the table can
///     record something false, the encode-side shape check does not catch the lie - it certifies
///     it, because the record is exactly what it compares against.
/// </summary>
public class ExtensionRegistrationTests
{
    private static void Validate(Type type, byte since = (byte)Dialect.V1) =>
        ExtensionDispatchBuilder.ValidateRegistration(type, "ExtensionServerOpcodeAttribute",
            typeof(IExtensionServerPacket), 0x0250, since);

    [Fact]
    public void Parse_ReturningAnotherPacketType_IsRejected()
    {
        // Both types are IExtensionPacket, so requiring only "some IExtensionPacket" would let
        // the table record a decoder-to-type mapping that is false — and ValidateShape compares
        // against exactly that record.
        var act = () => ExtensionDispatchBuilder.BindParse(typeof(TestParseReturnsAnotherTypePacket));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*declares Parse returning*TestServerNoncePacket*rather than*");
    }

    [Fact]
    public void Parse_Missing_IsRejected()
    {
        var act = () => ExtensionDispatchBuilder.BindParse(typeof(TestNoParsePacket));

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not declare*Parse*");
    }

    [Fact]
    public void Parse_ReturningItsOwnType_IsAccepted()
    {
        // Positive control: the check must accept the correct shape, or it is indistinguishable
        // from a blanket refusal.
        var decode = ExtensionDispatchBuilder.BindParse(typeof(TestServerNoncePacket));

        decode.Should().NotBeNull();
        decode(stackalloc byte[4]).Should().BeOfType<TestServerNoncePacket>();
    }

    [Theory]
    [InlineData(0x00)] // resolves for every frame - the dangerous one
    [InlineData(0xAA)] // retail's framing marker
    [InlineData(0xAF)] // the deliberate buffer below 0xB0
    [InlineData(0xFF)] // never allocated
    public void Since_OutsideTheDialectRange_IsRejected(byte since)
    {
        var act = () => Validate(typeof(TestSinceOutOfRangePacket), since);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not an allocatable dialect*");
    }

    [Fact]
    public void Since_InsideTheDialectRange_IsAccepted()
    {
        var withinRange = () => Validate(typeof(TestSinceOutOfRangePacket), (byte)Dialect.V1);
        withinRange.Should().NotThrow();

        var atCeiling = () => Validate(typeof(TestSinceOutOfRangePacket), 0xFE);
        atCeiling.Should().NotThrow();
    }

    [Fact]
    public void TypeNotImplementingTheDirectionMarker_IsRejected()
    {
        var act = () => ExtensionDispatchBuilder.ValidateRegistration(
            typeof(TestClientBytePacket), "ExtensionServerOpcodeAttribute",
            typeof(IExtensionServerPacket), 0x0201, (byte)Dialect.V1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement IExtensionServerPacket*");
    }

    [Fact]
    public void TheLibrarysOwnPacketsAllSatisfyTheseInvariants()
    {
        // The positive control at the level that matters: building the real codec runs every
        // check above over every declared packet in this assembly and in the library.
        var build = () => TestCodec.WithTestPackets();

        build.Should().NotThrow();
    }
}
