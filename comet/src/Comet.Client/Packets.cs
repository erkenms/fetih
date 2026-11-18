namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Packet types the test client sends or reads (patch 5619).
    /// </summary>
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
        public const ushort MsgPlayer = 10014;
    }

    /// <summary>Talk channels used during login and character creation.</summary>
    public static class TalkChannels
    {
        public const ushort Register = 2100;
        public const ushort Login = 2101;
    }

    /// <summary>A chat or system message (MsgTalk).</summary>
    public sealed record TalkMessage(ushort Channel, string Sender, string Recipient, string Text)
    {
        public static TalkMessage Parse(byte[] packet)
        {
            var strings = Packet.ReadStrings(packet, 24);
            string At(int i) => i < strings.Count ? strings[i] : "";
            return new TalkMessage(BitConverter.ToUInt16(packet, 8), At(0), At(1), At(3));
        }
    }

    /// <summary>The player's own character (MsgUserInfo), as the client reads it.</summary>
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

    /// <summary>Another player coming into view (MsgPlayer), as the 5619 client reads it.</summary>
    public sealed record PlayerSpawn(
        uint Id, uint Mesh, ushort X, ushort Y, ushort Hairstyle, byte Direction,
        ushort Level, ushort Class, string Name)
    {
        public static PlayerSpawn Parse(byte[] packet) => new PlayerSpawn(
            BitConverter.ToUInt32(packet, 8), BitConverter.ToUInt32(packet, 4),
            BitConverter.ToUInt16(packet, 92), BitConverter.ToUInt16(packet, 94),
            BitConverter.ToUInt16(packet, 96), packet[98],
            BitConverter.ToUInt16(packet, 107), BitConverter.ToUInt16(packet, 219),
            Packet.ReadStrings(packet, 226)[0]);
    }

    /// <summary>A general action (MsgAction), 38 bytes in 5619.</summary>
    public sealed record ActionMessage(
        uint CharacterId, uint Command, ushort Action, ushort X, ushort Y)
    {
        public const ushort LoginSpawn = 74;
        public const ushort LoginComplete = 251;
        public const ushort RemoveEntity = 135;

        public static ActionMessage Parse(byte[] packet) => new ActionMessage(
            BitConverter.ToUInt32(packet, 4), BitConverter.ToUInt32(packet, 8),
            BitConverter.ToUInt16(packet, 20), BitConverter.ToUInt16(packet, 24),
            BitConverter.ToUInt16(packet, 26));

        /// <summary>
        /// Builds LoginComplete the way the 5619 client sends it: ID 0, and 1 where the
        /// timestamp usually is (seen in VM).
        /// </summary>
        public static byte[] BuildLoginComplete()
        {
            var packet = Packet.Create(PacketTypes.MsgAction, 38);
            BitConverter.TryWriteBytes(packet.AsSpan(16), 1u);
            BitConverter.TryWriteBytes(packet.AsSpan(20), LoginComplete);
            return packet;
        }

        public static byte[] Build(uint characterId, ushort action)
        {
            var packet = Packet.Create(PacketTypes.MsgAction, 38);
            BitConverter.TryWriteBytes(packet.AsSpan(4), characterId);
            BitConverter.TryWriteBytes(packet.AsSpan(16), (uint)Environment.TickCount);
            BitConverter.TryWriteBytes(packet.AsSpan(20), action);
            return packet;
        }
    }

    /// <summary>Helpers for building and reading packets.</summary>
    public static class Packet
    {
        /// <summary>Creates a zeroed packet with its length and type set.</summary>
        public static byte[] Create(ushort type, int length)
        {
            var packet = new byte[length];
            BitConverter.TryWriteBytes(packet.AsSpan(0), (ushort)length);
            BitConverter.TryWriteBytes(packet.AsSpan(2), type);
            return packet;
        }

        /// <summary>Reads TQ's string list: a count, then length-prefixed strings.</summary>
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

        /// <summary>Writes text into a fixed-size, zero-padded field.</summary>
        public static void WriteText(byte[] packet, int offset, byte[] text, int size)
        {
            if (text.Length > size)
                throw new ArgumentException("Text longer than its field", nameof(text));
            text.CopyTo(packet, offset);
        }

        /// <summary>Reads a zero-terminated text field.</summary>
        public static string ReadText(byte[] packet, int offset, int size)
        {
            int end = Array.IndexOf(packet, (byte)0, offset, size);
            return Encoding.Latin1.GetString(packet, offset, (end < 0 ? offset + size : end) - offset);
        }
    }
}
