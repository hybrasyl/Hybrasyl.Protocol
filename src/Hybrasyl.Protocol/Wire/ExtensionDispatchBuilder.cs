using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DALib.Networking.Wire;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Builds the two <see cref="ExtensionDispatchTable" />s (C-&gt;S and S-&gt;C) by scanning
///     assemblies for both retail DALib packets and native extension packets.
/// </summary>
/// <remarks>
///     <para>
///         This reimplements the scan-and-bind pattern DALib uses internally, over DALib's
///         <em>public</em> surface: DALib's <c>PacketDispatchBuilder</c>, its parse delegate, and
///         its frame constants are all <c>internal</c>, and <c>PacketCodec</c>'s public entry
///         points parse whole <c>0xAA</c> frames, not bare bodies. Reimplementing here keeps DALib
///         genuinely unchanged, which the canonicality ruling requires.
///     </para>
///     <para>
///         Retail packets (DALib's <c>[ClientOpcode]</c>/<c>[ServerOpcode]</c>) register at their
///         byte opcode zero-extended to <c>u16</c>, introduced at signature <c>0xAA</c>. Native
///         extension packets register at their <c>u16</c> opcode and their declared
///         <c>Since</c> dialect.
///     </para>
/// </remarks>
internal static class ExtensionDispatchBuilder
{
    private const byte RetailIntroSignature = 0xAA;

    private delegate IPacket RetailParseFn(ReadOnlySpan<byte> body);

    private delegate IExtensionPacket ExtensionParseFn(ReadOnlySpan<byte> body);

    internal static (ExtensionDispatchTable Client, ExtensionDispatchTable Server) Build(
        IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var client = new ExtensionDispatchTable("C->S");
        var server = new ExtensionDispatchTable("S->C");

        foreach (var assembly in assemblies)
        {
            foreach (var type in LoadableTypes(assembly))
            {
                if (type is null)
                    continue;

                AddRetail<ClientOpcodeAttribute, IClientPacket>(
                    client, type, attr => attr.Opcode);
                AddRetail<ServerOpcodeAttribute, IServerPacket>(
                    server, type, attr => attr.Opcode);

                AddExtension<ExtensionClientOpcodeAttribute, IExtensionClientPacket>(
                    client, type, attr => attr.Opcode, attr => attr.Since);
                AddExtension<ExtensionServerOpcodeAttribute, IExtensionServerPacket>(
                    server, type, attr => attr.Opcode, attr => attr.Since);
            }
        }

        return (client, server);
    }

    private static void AddRetail<TAttribute, TMarker>(
        ExtensionDispatchTable table,
        Type type,
        Func<TAttribute, byte> opcodeOf)
        where TAttribute : Attribute
        where TMarker : IPacket
    {
        var attr = type.GetCustomAttribute<TAttribute>(inherit: false);

        if (attr is null)
            return;

        var opcode = opcodeOf(attr);

        if (!typeof(TMarker).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"{type.FullName} carries {typeof(TAttribute).Name} for opcode 0x{opcode:X2} " +
                $"but does not implement {typeof(TMarker).Name}.");

        var parse = BindParse<RetailParseFn>(type, typeof(IPacket));

        DecodedPacket Decode(ReadOnlySpan<byte> body) => DecodedPacket.FromRetail(parse(body));

        table.Add(opcode, RetailIntroSignature, Decode, type.FullName ?? type.Name);
    }

    private static void AddExtension<TAttribute, TMarker>(
        ExtensionDispatchTable table,
        Type type,
        Func<TAttribute, ushort> opcodeOf,
        Func<TAttribute, Dialect> sinceOf)
        where TAttribute : Attribute
        where TMarker : IExtensionPacket
    {
        var attr = type.GetCustomAttribute<TAttribute>(inherit: false);

        if (attr is null)
            return;

        var opcode = opcodeOf(attr);
        var introSignature = (byte)sinceOf(attr);

        if (!typeof(TMarker).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"{type.FullName} carries {typeof(TAttribute).Name} for opcode 0x{opcode:X4} " +
                $"but does not implement {typeof(TMarker).Name}.");

        var parse = BindParse<ExtensionParseFn>(type, typeof(IExtensionPacket));

        DecodedPacket Decode(ReadOnlySpan<byte> body) => DecodedPacket.FromExtension(parse(body));

        table.Add(opcode, introSignature, Decode, type.FullName ?? type.Name);
    }

    private static TDelegate BindParse<TDelegate>(Type type, Type requiredReturn)
        where TDelegate : Delegate
    {
        var parseMethod = type.GetMethod(
            "Parse",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            [typeof(ReadOnlySpan<byte>)]);

        if (parseMethod is null || !requiredReturn.IsAssignableFrom(parseMethod.ReturnType))
            throw new InvalidOperationException(
                $"{type.FullName} is registered as a packet but does not declare " +
                $"'public static {requiredReturn.Name} Parse(ReadOnlySpan<byte>)'.");

        // Return-type covariance: the method returns a concrete packet; the delegate returns the
        // interface. CreateDelegate accepts this.
        return (TDelegate)parseMethod.CreateDelegate(typeof(TDelegate));
    }

    private static IEnumerable<Type?> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A missing optional dependency in one type should not kill the whole scan.
            return ex.Types.Where(t => t is not null);
        }
    }
}
