#nullable enable
namespace Fetih.Network.Packets;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

public sealed class PacketDecoder
{
    private readonly Dictionary<PacketType, Type> packetClasses;
    private readonly Func<PacketType, IPacket?>? createHandler;

    private PacketDecoder(Dictionary<PacketType, Type> packetClasses, Func<PacketType, IPacket?>? createHandler)
    {
        this.packetClasses = packetClasses;
        this.createHandler = createHandler;
    }

    public static PacketDecoder For<TClient>(Func<PacketType, IPacket?>? createHandler = null)
    {
        var baseType = typeof(MsgBase<>).MakeGenericType(typeof(TClient));
        var classes = new Dictionary<PacketType, Type>();
        foreach (var type in typeof(TClient).Assembly.GetTypes())
        {
            if (type.IsAbstract || !baseType.IsAssignableFrom(type))
                continue;
            if (!Enum.TryParse<PacketType>(type.Name, out var packetType) || !HasDecode(type))
                continue;
            classes.TryAdd(packetType, type);
        }
        return new PacketDecoder(classes, createHandler);
    }

    public IPacket? Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 4)
            return null;

        var type = (PacketType)BitConverter.ToUInt16(packet[2..]);
        IPacket? instance = this.createHandler?.Invoke(type);
        if (instance != null && !HasDecode(instance.GetType()))
            instance = null;

        if (instance == null && this.packetClasses.TryGetValue(type, out var packetClass))
        {
            instance = (IPacket)(packetClass.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is { } ctor
                ? ctor.Invoke(null)
                : RuntimeHelpers.GetUninitializedObject(packetClass));
        }

        if (instance == null)
            return null;

        try
        {
            instance.Decode(packet.ToArray());
            return instance;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasDecode(Type packetClass)
    {
        var declaring = packetClass.GetMethod(nameof(IPacket.Decode), [typeof(byte[])])?.DeclaringType;
        return declaring != null && !(declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(MsgBase<>));
    }
}
