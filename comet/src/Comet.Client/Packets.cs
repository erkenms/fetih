namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static class PacketTypes
    {
        public const ushort MsgRegister = 1001;
        public const ushort MsgTalk = 1004;
        public const ushort MsgUserInfo = 1006;
        public const ushort MsgItem = 1009;
        public const ushort MsgConnect = 1052;
        public const ushort MsgConnectEx = 1055;
        public const ushort MsgEncryptCode = 1059;
        public const ushort MsgAccountSRP6 = 1124;
        public const ushort MsgLoginChallengeS = 1213;
        public const ushort MsgLoginProofC = 1214;
        public const ushort MsgAction = 10010;
        public const ushort MsgWalk = 10005;
        public const ushort MsgPlayer = 10014;
    }

    public static class TalkChannels
    {
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
    }

    public sealed record UserInfo(
        uint Id, uint Mesh, ushort Hairstyle, uint Silver, ushort Strength, ushort Agility,
        ushort Vitality, ushort Spirit, ushort HealthPoints, byte Level, byte Class, string Name)
    {
        public static UserInfo Parse(byte[] packet) => new UserInfo(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 10),
            BitConverter.ToUInt16(packet, 14), BitConverter.ToUInt32(packet, 16),
            BitConverter.ToUInt16(packet, 52), BitConverter.ToUInt16(packet, 54),
            BitConverter.ToUInt16(packet, 56), BitConverter.ToUInt16(packet, 58),
            BitConverter.ToUInt16(packet, 62), packet[68], packet[69],
            Packet.ReadStrings(packet, 110)[0]);
    }

    public sealed record PlayerSpawn(
        uint Id, uint Mesh, ushort X, ushort Y, ushort Hairstyle, byte Direction,
        uint Stance, ushort Level, ushort Class, string Name)
    {
        public static PlayerSpawn Parse(byte[] packet) => new PlayerSpawn(
            BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 4),
            BitConverter.ToUInt16(packet, 92), BitConverter.ToUInt16(packet, 94),
            BitConverter.ToUInt16(packet, 96), packet[98], BitConverter.ToUInt32(packet, 99),
            BitConverter.ToUInt16(packet, 107), BitConverter.ToUInt16(packet, 219),
            Packet.ReadStrings(packet, 226)[0]);
    }

    public sealed record ActionMessage(
        uint CharacterId, uint Command, ushort Action, ushort Direction, ushort X, ushort Y)
    {
        public const ushort LoginSpawn = 74;
        public const ushort Turn = 79;
        public const ushort Emote = 81;
        public const ushort Teleport = 86;
        public const ushort RemoveEntity = 135;
        public const ushort Jump = 137;
        public const ushort LoginComplete = 251;

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
