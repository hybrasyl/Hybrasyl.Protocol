using System;
using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     The result of decoding one extension frame. It is a union: the frame resolved either to a
///     retail-mirrored DALib packet (<see cref="Retail" />, introduced at signature <c>0xAA</c>)
///     or to a native extension packet (<see cref="Extension" />). Exactly one is non-null.
/// </summary>
/// <remarks>
///     Two hierarchies meet here because DALib's <see cref="IPacket" /> cannot be made to
///     implement a sreang interface without editing DALib (which the canonicality ruling
///     forbids). Consumers typically switch on the concrete runtime type via <see cref="Packet" />
///     regardless of which arm carried it.
/// </remarks>
public readonly record struct DecodedPacket
{
    private DecodedPacket(IPacket? retail, IExtensionPacket? extension)
    {
        Retail = retail;
        Extension = extension;
    }

    /// <summary>The retail-mirrored DALib packet, or null if this frame carried an extension
    ///     packet.</summary>
    public IPacket? Retail { get; }

    /// <summary>The native extension packet, or null if this frame carried a retail-mirrored
    ///     packet.</summary>
    public IExtensionPacket? Extension { get; }

    /// <summary>True if this frame resolved to a retail-mirrored DALib packet.</summary>
    public bool IsRetail => Retail is not null;

    /// <summary>True if this frame resolved to a native extension packet.</summary>
    public bool IsExtension => Extension is not null;

    /// <summary>The decoded packet as an <see cref="object" />, whichever arm carried it. Switch
    ///     on the concrete type.</summary>
    public object Packet => (object?)Retail ?? Extension!;

    /// <summary>The packet's opcode as a <see cref="ushort" /> (retail opcodes are zero-extended).</summary>
    public ushort Opcode => Retail is not null ? Retail.Opcode : Extension!.Opcode;

    /// <summary>Wraps a retail-mirrored DALib packet.</summary>
    public static DecodedPacket FromRetail(IPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return new DecodedPacket(packet, null);
    }

    /// <summary>Wraps a native extension packet.</summary>
    public static DecodedPacket FromExtension(IExtensionPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return new DecodedPacket(null, packet);
    }
}
