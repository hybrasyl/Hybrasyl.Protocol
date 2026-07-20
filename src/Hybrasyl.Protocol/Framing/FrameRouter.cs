using System;

namespace Hybrasyl.Protocol.Framing;

/// <summary>What the first byte of a buffer says about how to parse the frame that follows.</summary>
public enum FrameKind
{
    /// <summary>The buffer is empty; read more before routing.</summary>
    NeedMoreData,

    /// <summary>A retail DOOMVAS v1 frame (starts <c>0xAA</c>) - hand to DALib's codec.</summary>
    Retail,

    /// <summary>An extended-framing frame - hand to <see cref="Wire.ExtensionCodec" />.</summary>
    Extension,
}

/// <summary>
///     The first-byte router for a consumer read loop. Retail frames begin <c>0xAA</c>; an
///     extension frame begins with the high byte of its big-endian length, which is <c>0x00</c>
///     under the frame-size cap - so it can never be mistaken for retail. This lives in the shared
///     library, not DALib (DALib stays the pure retail codec).
/// </summary>
/// <remarks>
///     A connection can carry retail frames inside TLS too (the below-floor "0xAA-over-TLS" mode),
///     so routing by content rather than by connection flag is what keeps that case correct: the
///     same loop handles a retail frame and an extension frame on the same stream.
/// </remarks>
public static class FrameRouter
{
    /// <summary>The retail DOOMVAS v1 outer frame marker.</summary>
    public const byte RetailMarker = 0xAA;

    /// <summary>
    ///     Classifies the frame at the front of <paramref name="buffer" /> by its first byte
    ///     without consuming anything.
    /// </summary>
    public static FrameKind Peek(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return FrameKind.NeedMoreData;

        return buffer[0] == RetailMarker ? FrameKind.Retail : FrameKind.Extension;
    }
}
