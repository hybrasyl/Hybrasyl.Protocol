using System;
using System.IO;
using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Packets;

/// <summary>
///     The <see cref="AttributeBlockFlags.Status" /> block of an <see cref="Attributes" /> body:
///     the standalone state retail carried in its flag byte, promoted to a block of its own.
/// </summary>
/// <remarks>
///     The booleans are written as <c>1</c> and read as "non-zero", so a receiver accepts any
///     non-zero encoding. Retail packed mail and parcel as two nibbles of one byte, decoded only
///     when a separate flag bit was set; that cross-field dependency does not survive here.
/// </remarks>
/// <param name="MovementMode">A value 0-<see cref="MaxMovementMode" />, not a bitfield. Modes 1
///     and 2 make the client bypass tile and entity collision validation; modes 0 and 3 apply
///     normal collision. A swimming animation is an incidental side effect of a non-zero mode,
///     not its purpose.</param>
/// <param name="Blinded">Whether the character is blinded.</param>
/// <param name="HasUnreadMail">Whether unread mail is waiting.</param>
/// <param name="HasParcel">Whether a parcel is waiting.</param>
public sealed record AttributesStatus(
    byte MovementMode,
    bool Blinded,
    bool HasUnreadMail,
    bool HasParcel)
{
    /// <summary>The exact size of this block on the wire.</summary>
    public const int BlockLength = 4;

    /// <summary>
    ///     The highest allocated movement mode. Higher values are reserved, and are refused on
    ///     both read and write.
    /// </summary>
    /// <remarks>
    ///     Rejected rather than ignored because the modes select collision behaviour: a receiver
    ///     that dropped an unrecognised mode would silently apply collision where the sender
    ///     meant it bypassed. Allocating a further mode is a dialect version bump.
    /// </remarks>
    public const byte MaxMovementMode = 3;

    /// <exception cref="InvalidOperationException"><see cref="MovementMode" /> is reserved.</exception>
    internal void Write(IPacketWriter writer)
    {
        if (MovementMode > MaxMovementMode)
            throw new InvalidOperationException(
                $"Movement mode {MovementMode} is reserved; only 0-{MaxMovementMode} are "
                + "allocated, and a peer rejects the frame rather than ignoring the value.");

        writer.WriteByte(MovementMode);
        writer.WriteBoolean(Blinded);
        writer.WriteBoolean(HasUnreadMail);
        writer.WriteBoolean(HasParcel);
    }

    /// <exception cref="InvalidDataException">The encoded movement mode is reserved.</exception>
    internal static AttributesStatus Read(ref PacketReader reader)
    {
        var movementMode = reader.ReadByte();

        if (movementMode > MaxMovementMode)
            throw new InvalidDataException(
                $"Attributes Status carries movement mode {movementMode}; only "
                + $"0-{MaxMovementMode} are allocated.");

        return new AttributesStatus(movementMode,
            reader.ReadBoolean(),
            reader.ReadBoolean(),
            reader.ReadBoolean());
    }
}
