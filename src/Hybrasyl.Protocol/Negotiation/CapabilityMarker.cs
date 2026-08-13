using System;
using DALib.Networking.Packets.Server;

namespace Hybrasyl.Protocol.Negotiation;

/// <summary>
///     The capability marker a TLS-capable Hybrasyl server appends to its retail <c>0x7E</c>
///     lobby greeting to signal the extended protocol. Retail has no <c>0x7E</c> handler and
///     ignores the packet regardless of body, so the marker is invisible to retail clients; a
///     capable client (Brigid) detects it and answers by starting a TLS handshake.
/// </summary>
/// <remarks>
///     <para>
///         Wire form appended after the greeting body: <c>[0x00]["HYB"][u8 version][u8 flags]</c>.
///         The leading <c>0x00</c> separates the binary marker from the ASCII banner (which has no
///         terminator), and <c>"HYB"</c> is legible in a capture. Vanilla/Chaos <c>0x7E</c> never
///         emits this sequence.
///     </para>
///     <para>
///         <see cref="Version" /> versions the marker envelope itself, independently of the
///         dialect (which is negotiated inside TLS). A reader that sees a newer marker version it
///         does not understand should still treat the marker's presence as capability and upgrade -
///         the details are settled inside TLS, not here.
///     </para>
///     <para>
///         The dialect range is deliberately absent - it belongs inside the TLS channel
///         (DialectOffer), keeping the plaintext greeting free of any downgrade surface.
///     </para>
/// </remarks>
/// <param name="Version">The marker envelope version.</param>
/// <param name="Flags">Capability flags (all reserved in v1).</param>
public readonly record struct CapabilityMarker(byte Version, CapabilityFlags Flags)
{
    /// <summary>
    ///     The magic prefix: <c>[0x00]["HYB"]</c>. The NUL delimits the marker from the ASCII
    ///     greeting banner; the tag is legible in a capture and never emitted by vanilla/Chaos.
    /// </summary>
    public static ReadOnlySpan<byte> Magic => [0x00, 0x48, 0x59, 0x42];

    /// <summary>The current marker envelope version.</summary>
    public static readonly byte CurrentVersion = 0x01;

    /// <summary>Total marker length on the wire: magic (4) + version (1) + flags (1).</summary>
    public static readonly int Length = 6;

    /// <summary>The marker a current server appends: <see cref="CurrentVersion" />, no flags.</summary>
    public static CapabilityMarker Current => new(CurrentVersion, CapabilityFlags.None);

    /// <summary>Serialises just the marker bytes (<see cref="Length" /> long).</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[Length];

        Magic.CopyTo(bytes);
        bytes[Magic.Length] = Version;
        bytes[Magic.Length + 1] = (byte)Flags;

        return bytes;
    }

    /// <summary>
    ///     Builds the full S-&gt;C <c>0x7E</c> greeting body - the retail-canonical greeting from
    ///     DALib's <see cref="AcceptConnectionPacket" /> with this marker appended.
    /// </summary>
    public byte[] BuildGreetingBody() => BuildGreetingBody(new AcceptConnectionPacket());

    /// <summary>
    ///     Builds the full S-&gt;C <c>0x7E</c> greeting body from a specific
    ///     <paramref name="greeting" /> with this marker appended.
    /// </summary>
    public byte[] BuildGreetingBody(AcceptConnectionPacket greeting)
    {
        ArgumentNullException.ThrowIfNull(greeting);

        var greetingBytes = greeting.ToBody();
        var markerBytes = ToBytes();
        var body = new byte[greetingBytes.Length + markerBytes.Length];

        greetingBytes.CopyTo(body, 0);
        markerBytes.CopyTo(body, greetingBytes.Length);

        return body;
    }

    /// <summary>
    ///     Detects a capability marker in a received <c>0x7E</c> greeting body by scanning for
    ///     <see cref="Magic" />. Returns false for a retail/Chaos greeting (no marker) or a
    ///     truncated marker.
    /// </summary>
    /// <param name="greetingBody">The full <c>0x7E</c> packet body (after opcode/framing).</param>
    /// <param name="marker">The parsed marker, when the return value is true.</param>
    public static bool TryRead(ReadOnlySpan<byte> greetingBody, out CapabilityMarker marker)
    {
        marker = default;

        var magicIndex = greetingBody.IndexOf(Magic);

        if (magicIndex < 0)
            return false;

        var fieldsStart = magicIndex + Magic.Length;

        // Magic present but version/flags truncated - not a usable marker.
        if (fieldsStart + 2 > greetingBody.Length)
            return false;

        marker = new CapabilityMarker(greetingBody[fieldsStart], (CapabilityFlags)greetingBody[fieldsStart + 1]);

        return true;
    }
}
