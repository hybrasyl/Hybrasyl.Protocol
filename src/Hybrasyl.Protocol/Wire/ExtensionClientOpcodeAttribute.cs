using System;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Marks a native client-to-server extension packet with its <c>u16</c> opcode and the
///     dialect at which this packet's <em>format was introduced</em>. Mirrors DALib's
///     <c>[ClientOpcode]</c>, but for the extension opcode space.
/// </summary>
/// <remarks>
///     Resolution is latest-wins: an incoming <c>(dialect, opcode)</c> binds to the registered
///     type with the highest introduction <see cref="Since" /> that is <c>&lt;= dialect</c>. So
///     a dialect that changes N packets is N new types carrying the new <see cref="Since" /> and
///     zero edits to the unchanged ones.
/// </remarks>
/// <param name="opcode">The <c>u16</c> opcode. New packets are allocated up from <c>0x0100</c>.</param>
/// <param name="since">The dialect at which this packet shape was introduced.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExtensionClientOpcodeAttribute(ushort opcode, Dialect since) : Attribute
{
    /// <summary>The <c>u16</c> opcode.</summary>
    public ushort Opcode { get; } = opcode;

    /// <summary>The dialect at which this packet shape was introduced.</summary>
    public Dialect Since { get; } = since;
}
