using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Hybrasyl.Protocol.Wire;

/// <summary>
///     Builds the two <see cref="ExtensionDispatchTable" />s (C-&gt;S and S-&gt;C) by scanning
///     assemblies for declared extension packets.
/// </summary>
/// <remarks>
///     <para>
///         Only <em>declared</em> extension packets are registered - new packets (<c>0x0100+</c>)
///         and explicit replacements of a retail opcode with an upgraded shape (e.g. a <c>0x0015</c>
///         that upgrades a field to <c>u16</c>). Un-migrated retail packets are <strong>not</strong>
///         composed here: they travel as literal <c>0xAA</c> frames on DALib's codec (inside TLS or
///         not), routed away from the extension codec by the first-byte router. Nothing about the
///         retail packet set enters this dispatch.
///     </para>
///     <para>
///         The scan-and-bind mirrors DALib's own internal pattern (its builder and delegate are
///         <c>internal</c>), binding each type's public
///         <c>static T Parse(ReadOnlySpan&lt;byte&gt;)</c>.
///     </para>
/// </remarks>
internal static class ExtensionDispatchBuilder
{
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

                Add<ExtensionClientOpcodeAttribute, IExtensionClientPacket>(
                    client, type, attr => attr.Opcode, attr => attr.Since);
                Add<ExtensionServerOpcodeAttribute, IExtensionServerPacket>(
                    server, type, attr => attr.Opcode, attr => attr.Since);
            }
        }

        return (client, server);
    }

    private static void Add<TAttribute, TMarker>(
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

        table.Add(opcode, introSignature, BindParse(type), type.FullName ?? type.Name);
    }

    private static ExtensionDecodeFn BindParse(Type type)
    {
        var parseMethod = type.GetMethod(
            "Parse",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            [typeof(ReadOnlySpan<byte>)]);

        if (parseMethod is null || !typeof(IExtensionPacket).IsAssignableFrom(parseMethod.ReturnType))
            throw new InvalidOperationException(
                $"{type.FullName} is registered as an extension packet but does not declare " +
                $"'public static {type.Name} Parse(ReadOnlySpan<byte>)'.");

        // Return-type covariance: Parse returns the concrete type; the delegate returns the
        // interface. CreateDelegate accepts this.
        return (ExtensionDecodeFn)parseMethod.CreateDelegate(typeof(ExtensionDecodeFn));
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
