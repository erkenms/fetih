namespace Fetih.Web.Features.Packets;

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Fetih.TestClient;
using Fetih.Network.Packets;
using AccountClient = Fetih.Account.States.Client;
using AccountHandlers = Fetih.Account.Packets.PacketHandlers;
using GameClient = Fetih.Game.States.Client;
using GameHandlers = Fetih.Game.Packets.PacketHandlers;

public enum PacketServer
{
    Game,
    Account,
}

public enum PacketDirection
{
    // Client to server
    In,
    // Server to client
    Out,
}

public enum PacketReaderKind
{
    // The server's own packet class, the same Decode the server runs on that packet
    Server,
    // The headless test client's reader, for packets the server only writes
    Client,
    None,
}

public record PacketDecodeRequest(string Hex, PacketServer Server = PacketServer.Game, PacketDirection Direction = PacketDirection.In);

public record PacketDecodeResponse(
    ushort Type,
    string? TypeName,
    int Length,
    int DeclaredLength,
    string? Seal,
    PacketReaderKind Reader,
    string? ReaderName,
    bool Handled,
    PacketField[] Fields,
    string? Error,
    string Dump);

public static class PacketDecoder
{
    public const int MaxBytes = 65536;

    // Server-to-client layouts the server only writes, or reads only as the client's request
    // (MsgPlayerAttribInfo): the test client's reader for each
    private static readonly Dictionary<PacketType, Func<byte[], object>> ClientReaders = new()
    {
        [PacketType.MsgUserInfo] = p => UserInfo.Parse(p),
        [PacketType.MsgPlayer] = p => PlayerSpawn.Parse(p),
        [PacketType.MsgMagicInfo] = p => MagicInfo.Parse(p),
        [PacketType.MsgWeaponSkill] = p => WeaponSkill.Parse(p),
        [PacketType.MsgMagicEffect] = p => MagicEffect.Parse(p),
        [PacketType.MsgUserAttrib] = p => UserAttribMessage.Parse(p),
        [PacketType.MsgItemInfo] = p => ItemInfo.Parse(p),
        [PacketType.MsgItemInfoEx] = p => BoothItem.Parse(p),
        [PacketType.MsgNpcInfo] = p => NpcSpawn.Parse(p),
        [PacketType.MsgNpcInfoEx] = p => GoalSpawn.Parse(p),
        [PacketType.MsgTeamMember] = p => TeamMemberMessage.Parse(p),
        [PacketType.MsgRelation] = p => RelationMessage.Parse(p),
        [PacketType.MsgTradeBuddyInfo] = p => TradeBuddyInfo.Parse(p),
        [PacketType.MsgPlayerAttribInfo] = p => StatWindow.Parse(p),
        [PacketType.MsgElitePKArenic] = p => ElitePkArenic.Parse(p),
        [PacketType.MsgTeamPKArenic] = p => TeamPkArenic.Parse(p),
        [PacketType.MsgTeamPKArenicScore] = p => TeamPkScore.Parse(p),
        [PacketType.MsgArenicScore] = p => ArenaScore.Parse(p),
        [PacketType.MsgDetainItemInfo] = p => DetainItemInfoMessage.Parse(p),
        [PacketType.MsgDetainItemUpdate] = p => DetainItemUpdateMessage.Parse(p),
        [PacketType.MsgTotemPoleInfo] = p => TotemPoleInfoMessage.Parse(p),
        [PacketType.MsgSyndicateAttributeInfo] = p => GuildInfo.Parse(p),
    };

    private static readonly Dictionary<string, Type> GamePacketClasses = PacketClasses(typeof(GameClient));
    private static readonly Dictionary<string, Type> AccountPacketClasses = PacketClasses(typeof(AccountClient));

    public static bool TryParseHex(string? text, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || c == '-' || c == ':')
                continue;
            if (!char.IsAsciiHexDigit(c))
                return false;
            sb.Append(c);
        }

        if (sb.Length < 8 || sb.Length % 2 != 0 || sb.Length / 2 > MaxBytes)
            return false;
        bytes = Convert.FromHexString(sb.ToString());
        return true;
    }

    public static PacketDecodeResponse Decode(byte[] packet, PacketServer server, PacketDirection direction)
    {
        ushort declared = BitConverter.ToUInt16(packet, 0);
        ushort type = BitConverter.ToUInt16(packet, 2);
        var packetType = (PacketType)type;
        string? typeName = Enum.IsDefined(packetType) ? packetType.ToString() : null;
        string? seal = ReadSeal(packet, declared);
        string dump = PacketDump.Hex(packet);

        bool handled = direction == PacketDirection.In && (server == PacketServer.Game
            ? GameHandlers.Handles(packetType)
            : AccountHandlers.Handles(packetType));

        object? decoded = null;
        string? readerName = null;
        var reader = PacketReaderKind.None;
        string? error = null;

        try
        {
            if (direction == PacketDirection.Out && server == PacketServer.Game &&
                ClientReaders.TryGetValue(packetType, out var read))
            {
                reader = PacketReaderKind.Client;
                decoded = read(packet);
                readerName = decoded.GetType().FullName;
            }
            else if (CreateServerPacket(server, direction, packetType, typeName) is { } serverPacket &&
                HasDecode(serverPacket.GetType()))
            {
                reader = PacketReaderKind.Server;
                readerName = serverPacket.GetType().FullName;
                decoded = serverPacket;
                serverPacket.Decode(packet);
            }
        }
        // Any packet can be pasted here, so any reader may fail on it
        catch (Exception ex)
        {
            error = (ex is TargetInvocationException { InnerException: { } inner } ? inner : ex).Message;
        }

        var fields = decoded == null ? [] : PacketFields.Describe(decoded,
            reader == PacketReaderKind.Server ? HeaderProperties : new HashSet<string>());
        return new PacketDecodeResponse(type, typeName, packet.Length, declared, seal, reader, readerName, handled,
            fields, error, dump);
    }

    // MsgBase's own header, already in the response
    private static readonly HashSet<string> HeaderProperties = new(StringComparer.Ordinal) { "Length", "Type" };

    private static IPacket? CreateServerPacket(PacketServer server, PacketDirection direction, PacketType type, string? typeName)
    {
        if (direction == PacketDirection.In)
        {
            IPacket? handler = server == PacketServer.Game ? GameHandlers.Create(type) : AccountHandlers.Create(type);
            if (handler != null)
                return handler;
        }

        if (typeName == null)
            return null;
        var classes = server == PacketServer.Game ? GamePacketClasses : AccountPacketClasses;
        if (!classes.TryGetValue(typeName, out var packetClass))
            return null;

        return (IPacket)(packetClass.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is { } ctor
            ? ctor.Invoke(null)
            : RuntimeHelpers.GetUninitializedObject(packetClass));
    }

    private static bool HasDecode(Type packetClass)
    {
        var declaring = packetClass.GetMethod(nameof(IPacket.Decode), [typeof(byte[])])?.DeclaringType;
        return declaring != null && !(declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(MsgBase<>));
    }

    private static Dictionary<string, Type> PacketClasses(Type clientType)
    {
        var baseType = typeof(MsgBase<>).MakeGenericType(clientType);
        return clientType.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t))
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    // The 8 ASCII bytes the client and server append after the declared length
    private static string? ReadSeal(byte[] packet, int declared)
    {
        if (declared < 4 || packet.Length <= declared)
            return null;
        var tail = packet.AsSpan(declared);
        foreach (byte b in tail)
            if (b < 32 || b > 126)
                return Convert.ToHexString(tail);
        return Encoding.ASCII.GetString(tail);
    }

    internal static string Invariant(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
