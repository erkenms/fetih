namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Broadcast queue page sent to the client.
    // Layout: Length (0), Type 2051 (2), Page (4, uint), Append (8, ushort: 0 clears the client's
    // list before adding, non-zero appends), Count (10, ushort), then Count 112-byte entries from 12:
    // ID (0, uint), Position (4, uint, 0-based), CharacterID (8, uint), SenderName (12, 16 bytes),
    // Addition (28, uint, extra CPs spent), Message (32, 80 bytes).
    public sealed class MsgPigeonQuery : MsgBase<Client>
    {
        public const int HeaderSize = 12;
        public const int EntrySize = 112;
        // Client CNetMsg buffer is 1024 bytes: 12 + 8 * 112 = 908
        public const int MaxEntries = 8;

        public uint Page { get; set; }
        public ushort Append { get; set; }
        public List<Entry> Entries { get; } = new List<Entry>();

        public MsgPigeonQuery() => base.Type = PacketType.MsgPigeonQuery;

        public MsgPigeonQuery(uint page, bool append, IEnumerable<Entry> entries) : this()
        {
            this.Page = page;
            this.Append = (ushort)(append ? 1 : 0);
            if (entries != null)
                this.Entries.AddRange(entries);
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Page = reader.ReadUInt32();
            this.Append = reader.ReadUInt16();
            ushort count = reader.ReadUInt16();
            this.Entries.Clear();
            for (int i = 0; i < count && HeaderSize + (i + 1) * EntrySize <= bytes.Length; i++)
            {
                uint id = reader.ReadUInt32();
                uint position = reader.ReadUInt32();
                uint characterID = reader.ReadUInt32();
                string sender = reader.ReadString(16);
                uint addition = reader.ReadUInt32();
                string message = reader.ReadString(80);
                this.Entries.Add(new Entry(id, position, characterID, sender, addition, message));
            }
        }

        public override byte[] Encode()
        {
            int count = Math.Min(this.Entries.Count, MaxEntries);
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Page);
            writer.Write(this.Append);
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                var entry = this.Entries[i];
                writer.Write(entry.ID);
                writer.Write(entry.Position);
                writer.Write(entry.CharacterID);
                WriteFixed(writer, entry.SenderName, 16);
                writer.Write(entry.Addition);
                WriteFixed(writer, entry.Message, 80);
            }
            return writer.ToArray();
        }

        // Leaves the final byte zero so the client's C-string reads never overrun the field
        private static void WriteFixed(PacketWriter writer, string value, int fixedLength)
        {
            var buffer = new byte[fixedLength];
            if (!string.IsNullOrEmpty(value))
            {
                var bytes = Encoding.Latin1.GetBytes(value);
                Buffer.BlockCopy(bytes, 0, buffer, 0, Math.Min(bytes.Length, fixedLength - 1));
            }
            writer.Write(buffer);
        }

        public readonly record struct Entry(
            uint ID, uint Position, uint CharacterID, string SenderName, uint Addition, string Message);
    }
}
