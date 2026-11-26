namespace Fetih.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Enemy list request/response. Client sends 44 bytes with its character ID at 4 when
    // opening the enemy dialog. Server replies with the character ID at 4, entry count at 8,
    // and 32-byte entries from 12: online byte (0), 3 bytes padding, enemy ID (4),
    // nobility rank (8), gender (12; 1 male, 2 female), name (16 bytes at 16).
    public sealed class MsgEnemyList : MsgBase<Client>
    {
        public const int HeaderSize = 12;
        public const int EntrySize = 32;
        public const int MinSize = HeaderSize + EntrySize;
        // Client CNetMsg buffer is 1024 bytes: 12 + 31 * 32 = 1004
        public const int MaxEntries = 31;

        public uint UserID { get; set; }
        public List<Entry> Entries { get; } = new List<Entry>();

        public MsgEnemyList() => base.Type = PacketType.MsgEnemyList;

        public MsgEnemyList(uint userID, IEnumerable<Entry> entries) : this()
        {
            this.UserID = userID;
            if (entries != null)
                this.Entries.AddRange(entries);
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.UserID = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            int count = Math.Min(this.Entries.Count, MaxEntries);
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.UserID);      // 4
            writer.Write(count);            // 8
            if (count == 0)
            {
                writer.Write(new byte[EntrySize]);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var entry = this.Entries[i];
                    writer.Write((byte)(entry.Online ? 1 : 0)); // +0
                    writer.Write(new byte[3]);                  // +1
                    writer.Write(entry.ID);                     // +4
                    writer.Write(entry.Nobility);               // +8
                    writer.Write(entry.Gender);                 // +12
                    writer.Write(entry.Name ?? string.Empty, 16); // +16
                }
            }
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Relationships.SendEnemyListAsync(client);

        public readonly record struct Entry(uint ID, string Name, bool Online, uint Nobility, int Gender);
    }
}
