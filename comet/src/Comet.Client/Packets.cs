namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public static class PacketTypes
    {
        public const ushort MsgRegister = 1001;
        public const ushort MsgTalk = 1004;
        public const ushort MsgUserInfo = 1006;
        public const ushort MsgItemInfo = 1008;
        public const ushort MsgItem = 1009;
        public const ushort MsgName = 1015;
        public const ushort MsgMapItem = 1101;
        public const ushort MsgAllot = 1024;
        public const ushort MsgConnect = 1052;
        public const ushort MsgConnectEx = 1055;
        public const ushort MsgEncryptCode = 1059;
        public const ushort MsgAccountSRP6 = 1124;
        public const ushort MsgLoginChallengeS = 1213;
        public const ushort MsgLoginProofC = 1214;
        public const ushort MsgAction = 10010;
        public const ushort MsgWalk = 10005;
        public const ushort MsgPlayer = 10014;
        public const ushort MsgUserAttrib = 10017;
    }

    public static class TalkChannels
    {
        public const ushort Talk = 2000;
        public const ushort Whisper = 2001;
        public const ushort Center = 2011;
        public const ushort Service = 2014;
        public const ushort World = 2021;
        public const ushort Register = 2100;
        public const ushort Login = 2101;
    }

    public sealed record TalkMessage(ushort Channel, string Sender, string Recipient, string Text)
    {
        public static TalkMessage Parse(byte[] packet)
        {
            var strings = Packet.ReadStrings(packet, 24);
            string At(int i) => i < strings.Count ? strings[i] : "";
            return new TalkMessage(BitConverter.ToUInt16(packet, 8), At(0), At(1), At(3));
        }

        // Color, channel, style, an ID, the recipient's and sender's mesh, then the strings
        // sender, recipient, suffix and text
        public static byte[] Build(ushort channel, string sender, string recipient, string text)
        {
            var strings = new[] { sender, recipient, "", text }
                .Select(s => Encoding.Latin1.GetBytes(s)).ToArray();
            var packet = Packet.Create(PacketTypes.MsgTalk, 25 + strings.Sum(s => 1 + s.Length));
            BitConverter.TryWriteBytes(packet.AsSpan(4), 0x00FFFFFF);
            BitConverter.TryWriteBytes(packet.AsSpan(8), channel);
            int offset = 24;
            packet[offset++] = (byte)strings.Length;
            foreach (var s in strings)
            {
                packet[offset++] = (byte)s.Length;
                s.CopyTo(packet, offset);
                offset += s.Length;
            }
            return packet;
        }
    }

    // A number, the action as one byte, then the strings
    public sealed record NameMessage(uint Data, byte Action, List<string> Strings)
    {
        public const byte WhisperDetails = 26;

        public static NameMessage Parse(byte[] packet) => new NameMessage(
            BitConverter.ToUInt32(packet, 4), packet[8], Packet.ReadStrings(packet, 9));

        public static byte[] Build(byte action, string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            var packet = Packet.Create(PacketTypes.MsgName, 11 + bytes.Length);
            packet[8] = action;
            packet[9] = 1;
            packet[10] = (byte)bytes.Length;
            bytes.CopyTo(packet, 11);
            return packet;
        }
    }

    public sealed record UserInfo(
        uint Id, uint Mesh, ushort Hairstyle, uint Silver, ushort Strength, ushort Agility,
        ushort Vitality, ushort Spirit, ushort HealthPoints, byte Level, byte Class, string Name,
        ulong Experience, ushort AttributePoints, ushort ManaPoints)
    {
        public static UserInfo Parse(byte[] packet) => new UserInfo(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 10),
            BitConverter.ToUInt16(packet, 14), BitConverter.ToUInt32(packet, 16),
            BitConverter.ToUInt16(packet, 52), BitConverter.ToUInt16(packet, 54),
            BitConverter.ToUInt16(packet, 56), BitConverter.ToUInt16(packet, 58),
            BitConverter.ToUInt16(packet, 62), packet[68], packet[69],
            Packet.ReadStrings(packet, 110)[0], BitConverter.ToUInt64(packet, 24),
            BitConverter.ToUInt16(packet, 60), BitConverter.ToUInt16(packet, 64));
    }

    public sealed record PlayerSpawn(
        uint Id, uint Mesh, ushort X, ushort Y, ushort Hairstyle, byte Direction,
        uint Stance, ushort Level, ushort Class, string Name,
        uint Helmet, uint Armor, uint RightHand, uint LeftHand)
    {
        public static PlayerSpawn Parse(byte[] packet) => new PlayerSpawn(
            BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 4),
            BitConverter.ToUInt16(packet, 92), BitConverter.ToUInt16(packet, 94),
            BitConverter.ToUInt16(packet, 96), packet[98], BitConverter.ToUInt32(packet, 99),
            BitConverter.ToUInt16(packet, 107), BitConverter.ToUInt16(packet, 219),
            Packet.ReadStrings(packet, 226)[0],
            BitConverter.ToUInt32(packet, 44), BitConverter.ToUInt32(packet, 52),
            BitConverter.ToUInt32(packet, 56), BitConverter.ToUInt32(packet, 60));
    }

    public sealed record ActionMessage(
        uint CharacterId, uint Command, ushort Action, ushort Direction, ushort X, ushort Y)
    {
        public const ushort LoginSpawn = 74;
        public const ushort LoginInventory = 75;
        public const ushort Turn = 79;
        public const ushort Emote = 81;
        public const ushort LevelUp = 92;
        public const ushort Portal = 85;
        public const ushort Teleport = 86;
        public const ushort RemoveEntity = 135;
        public const ushort Jump = 137;
        public const ushort LoginComplete = 251;
        public const ushort QueryStatInfo = 408;

        public static ActionMessage Parse(byte[] packet) => new ActionMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt16(packet, 20), BitConverter.ToUInt16(packet, 22),
            BitConverter.ToUInt16(packet, 24), BitConverter.ToUInt16(packet, 26));

        public static byte[] Build(uint characterId, ushort action, uint command = 0,
            int direction = 0, int x = 0, int y = 0)
        {
            var packet = Packet.Create(PacketTypes.MsgAction, 38);
            BitConverter.TryWriteBytes(packet.AsSpan(4), characterId);
            BitConverter.TryWriteBytes(packet.AsSpan(8), command);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (uint)Environment.TickCount);
            BitConverter.TryWriteBytes(packet.AsSpan(20), action);
            BitConverter.TryWriteBytes(packet.AsSpan(22), (ushort)direction);
            BitConverter.TryWriteBytes(packet.AsSpan(24), (ushort)x);
            BitConverter.TryWriteBytes(packet.AsSpan(26), (ushort)y);
            return packet;
        }

        public static byte[] BuildJump(uint characterId, int fromX, int fromY, int toX, int toY)
        {
            var packet = Build(characterId, Jump, (uint)(toX | (toY << 16)), x: fromX, y: fromY);
            BitConverter.TryWriteBytes(packet.AsSpan(32), -1);
            return packet;
        }

        public static byte[] BuildLoginComplete()
        {
            var packet = Packet.Create(PacketTypes.MsgAction, 38);
            BitConverter.TryWriteBytes(packet.AsSpan(16), 1u);
            BitConverter.TryWriteBytes(packet.AsSpan(20), LoginComplete);
            return packet;
        }
    }

    // An identity, a count, then per attribute a type (uint) and value (ulong) in 20 bytes
    public sealed record UserAttribMessage(uint Id, Dictionary<uint, ulong> Values)
    {
        public const uint Health = 0, MaxHealth = 1, Mana = 2, MaxMana = 3, Silver = 4,
            Experience = 5, AttributePoints = 10, Level = 12, Spirit = 13, Vitality = 14,
            Strength = 15, Agility = 16;

        public static UserAttribMessage Parse(byte[] packet)
        {
            var values = new Dictionary<uint, ulong>();
            uint count = BitConverter.ToUInt32(packet, 8);
            for (int i = 0, offset = 12; i < count && offset + 12 <= packet.Length; i++, offset += 20)
                values[BitConverter.ToUInt32(packet, offset)] = BitConverter.ToUInt64(packet, offset + 4);
            return new UserAttribMessage(BitConverter.ToUInt32(packet, 4), values);
        }
    }

    // Item ID, type, amount and its limit, then the action and position as ushorts
    public sealed record ItemInfo(uint Id, uint TypeId, ushort Amount, ushort AmountLimit, ushort Action, ushort Position)
    {
        public static ItemInfo Parse(byte[] packet) => new ItemInfo(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt16(packet, 12), BitConverter.ToUInt16(packet, 14),
            BitConverter.ToUInt16(packet, 16), BitConverter.ToUInt16(packet, 18));
    }

    // An item or character ID, a value (a slot when equipping), the action and a timestamp
    // DisplayGears carries the item IDs worn in slots 1 to 9 from offset 32
    public sealed record ItemMessage(uint Id, uint Data, uint Action, uint[] Gears)
    {
        public const uint Remove = 3, Equip = 4, Equipped = 5, Unequip = 6, Ping = 27, Drop = 37,
            DropSilver = 38, DisplayGears = 46;

        public static ItemMessage Parse(byte[] packet) => new ItemMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12),
            Enumerable.Range(0, 9).Select(i => BitConverter.ToUInt32(packet, 32 + 4 * i)).ToArray());

        public static byte[] Build(uint id, uint data, uint action)
        {
            var packet = Packet.Create(PacketTypes.MsgItem, 88);
            BitConverter.TryWriteBytes(packet.AsSpan(4), id);
            BitConverter.TryWriteBytes(packet.AsSpan(8), data);
            BitConverter.TryWriteBytes(packet.AsSpan(12), action);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (uint)Environment.TickCount);
            return packet;
        }
    }

    // A ground item's ID, its item type, cell, color and the action (1 shown, 2 gone, 3 picked up)
    public sealed record GroundItemMessage(uint Id, uint TypeId, ushort X, ushort Y, ushort Action)
    {
        public const ushort Create = 1, Delete = 2, Pick = 3;

        public static GroundItemMessage Parse(byte[] packet) => new GroundItemMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt16(packet, 12), BitConverter.ToUInt16(packet, 14),
            BitConverter.ToUInt16(packet, 18));

        public static byte[] BuildPick(uint id, int x, int y)
        {
            var packet = Packet.Create(PacketTypes.MsgMapItem, 24);
            BitConverter.TryWriteBytes(packet.AsSpan(4), id);
            BitConverter.TryWriteBytes(packet.AsSpan(12), (ushort)x);
            BitConverter.TryWriteBytes(packet.AsSpan(14), (ushort)y);
            BitConverter.TryWriteBytes(packet.AsSpan(18), Pick);
            return packet;
        }
    }

    // Points to add to strength, agility, vitality and spirit, a byte each
    public static class AllotMessage
    {
        public static byte[] Build(byte strength, byte agility, byte vitality, byte spirit)
        {
            var packet = Packet.Create(PacketTypes.MsgAllot, 8);
            packet[4] = strength;
            packet[5] = agility;
            packet[6] = vitality;
            packet[7] = spirit;
            return packet;
        }
    }

    public sealed record WalkMessage(uint Direction, uint CharacterId, uint Mode)
    {
        public static WalkMessage Parse(byte[] packet) => new WalkMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt32(packet, 12));

        public static byte[] Build(uint characterId, int direction)
        {
            var packet = Packet.Create(PacketTypes.MsgWalk, 24);
            BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)(8 * Random.Shared.Next(1, 30) + direction));
            BitConverter.TryWriteBytes(packet.AsSpan(8), characterId);
            BitConverter.TryWriteBytes(packet.AsSpan(12), 1u);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (uint)Environment.TickCount);
            return packet;
        }
    }

    public static class Packet
    {
        public static byte[] Create(ushort type, int length)
        {
            var packet = new byte[length];
            BitConverter.TryWriteBytes(packet.AsSpan(0), (ushort)length);
            BitConverter.TryWriteBytes(packet.AsSpan(2), type);
            return packet;
        }

        public static List<string> ReadStrings(byte[] packet, int offset)
        {
            var strings = new List<string>();
            int count = packet[offset++];
            for (int i = 0; i < count && offset < packet.Length; i++)
            {
                int length = packet[offset++];
                strings.Add(Encoding.Latin1.GetString(packet, offset, length));
                offset += length;
            }
            return strings;
        }

        public static void WriteText(byte[] packet, int offset, byte[] text, int size)
        {
            if (text.Length > size)
                throw new ArgumentException("Text longer than its field", nameof(text));
            text.CopyTo(packet, offset);
        }

        public static string ReadText(byte[] packet, int offset, int size)
        {
            int end = Array.IndexOf(packet, (byte)0, offset, size);
            return Encoding.Latin1.GetString(packet, offset, (end < 0 ? offset + size : end) - offset);
        }
    }
}
