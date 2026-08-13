using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hybrasyl.Protocol.Framing;

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
        var introDialect = (byte)sinceOf(attr);

        ValidateRegistration(type, typeof(TAttribute).Name, typeof(TMarker), opcode,
            introDialect);

        table.Add(opcode, introDialect, BindParse(type), type);
    }

    /// <summary>
    ///     The metadata invariants one registration must satisfy before it may enter a table.
    /// </summary>
    /// <remarks>
    ///     These are the conditions under which the table would record something false, which is
    ///     what the encode-side shape check trusts.
    /// </remarks>
    internal static void ValidateRegistration(
        Type type,
        string attributeName,
        Type markerInterface,
        ushort opcode,
        byte introDialect)
    {
        if (!markerInterface.IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"{type.FullName} carries {attributeName} for opcode 0x{opcode:X4} " +
                $"but does not implement {markerInterface.Name}.");

        // Since is a cast away from any byte, and resolution is "highest introduction <= the
        // frame's dialect" - so a shape introduced below 0xB0 would resolve for every frame,
        // including ones stamped with a dialect that never contained it.
        if (!ExtensionFrame.IsValidDialect(introDialect))
            throw new InvalidOperationException(
                $"{type.FullName} carries {attributeName} for opcode 0x{opcode:X4} " +
                $"with Since 0x{introDialect:X2}, which is not an allocatable dialect " +
                $"dialect (0x{ExtensionFrame.MinDialect:X2}.." +
                $"0x{ExtensionFrame.MaxDialect:X2}).");
    }

    internal static ExtensionDecodeFn BindParse(Type type)
    {
        var parseMethod = type.GetMethod(
            "Parse",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
            [typeof(ReadOnlySpan<byte>)]);

        if (parseMethod is null)
            throw new InvalidOperationException(
                $"{type.FullName} is registered as an extension packet but does not declare " +
                $"'public static {type.Name} Parse(ReadOnlySpan<byte>)'.");

        if (parseMethod.ReturnType != type)
            throw new InvalidOperationException(
                $"{type.FullName} declares Parse returning {parseMethod.ReturnType.FullName} " +
                $"rather than {type.FullName}. The dispatch table records the declaring type as " +
                "the shape the decoder produces; a mismatch makes that record false and the " +
                "encode-side shape check unsound.");

        // Parse returns the concrete type; CreateDelegate permits the covariant bind.
        return (ExtensionDecodeFn)parseMethod.CreateDelegate(typeof(ExtensionDecodeFn));
    }

    /// <summary>
    ///     Every type in <paramref name="assembly" />, or a failure - never a partial answer.
    /// </summary>
    /// <remarks>
    ///     Registering only the types that loaded would build an incomplete protocol that fails
    ///     later as an unregistered opcode on a live connection, and nothing here can tell whether
    ///     an unloaded type was a packet.
    /// </remarks>
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaded = ex.Types.Count(t => t is not null);
            var reasons = string.Join("; ",
                ex.LoaderExceptions.Where(e => e is not null).Select(e => e!.Message).Distinct());

            throw new InvalidOperationException(
                $"Could not load all types from {assembly.GetName().Name} while building the " +
                $"extension dispatch ({loaded} of {ex.Types.Length} loaded). Registering only the " +
                "types that loaded would build an incomplete protocol that fails later as an " +
                $"unregistered opcode on a live connection. Loader errors: {reasons}", ex);
        }
    }
}
