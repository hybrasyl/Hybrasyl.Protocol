using System;
using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     The common contract for a native extended-framing packet - one introduced by a dialect
///     rather than mirrored from retail. Unlike DALib's <see cref="IPacket" /> (whose opcode is a
///     <see cref="byte" />), an extension packet's opcode is a full <see cref="ushort" />: retail
///     numbers are zero-extended into <c>0x0000</c>-<c>0x00FF</c>, and entirely new packets are
///     allocated up from <c>0x0100</c>.
/// </summary>
/// <remarks>
///     <para>
///         Bodies use DALib's public <see cref="IPacketWriter" /> and the same
///         <c>public static T Parse(ReadOnlySpan&lt;byte&gt;)</c> convention as DALib packets, so an
///         extension packet body looks identical to a retail one at the serialization layer.
///         Framing, length, and the dialect are the codec's concern; crypto is TLS's.
///     </para>
///     <para>
///         <strong>Parsing is exact-body consumption.</strong> A <c>Parse</c> must reject a body it
///         does not consume entirely, not merely stop reading when it has what it wants. A short
///         body throws from the reader on its own; a <em>long</em> one is the dangerous direction,
///         because it parses cleanly while discarding the excess - so a framing bug, a version
///         mismatch, or a peer appending data all present as a valid packet. Reject rather than
///         truncate; the codec normalises the failure to
///         <see cref="System.IO.InvalidDataException" /> either way.
///     </para>
///     <para>
///         <c>Parse</c>'s return type must be the declaring type exactly. The dispatch table
///         records it as the shape that decoder produces, and the encode-side shape check compares
///         against that record - a <c>Parse</c> returning a sibling type would make the record
///         false rather than be caught by it. Registration refuses this at codec construction.
///     </para>
/// </remarks>
public interface IExtensionPacket
{
    /// <summary>The <c>u16</c> opcode identifying this packet within its dialect.</summary>
    ushort Opcode { get; }

    /// <summary>Writes the plaintext body - everything after the frame header - into
    ///     <paramref name="writer" />.</summary>
    void WriteBody(IPacketWriter writer);

    /// <summary>Returns the plaintext body as a freshly allocated array.</summary>
    byte[] ToBody();

    /// <summary>Returns the plaintext body as a <see cref="ReadOnlyMemory{T}" />.</summary>
    ReadOnlyMemory<byte> ToBodyMemory();
}

/// <summary>A native extension packet travelling client-to-server.</summary>
public interface IExtensionClientPacket : IExtensionPacket;

/// <summary>A native extension packet travelling server-to-client.</summary>
public interface IExtensionServerPacket : IExtensionPacket;
