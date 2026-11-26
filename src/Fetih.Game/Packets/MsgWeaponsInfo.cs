namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // Per-arsenal inscribed item list (2202). Client queries with 84 bytes: action (u16, 0) at 4,
    // 1-based beginAt at 8, 1-based inclusive endAt at 12, totem type at 16, count (0) at 40.
    // Server replies with 44 + count * 40 bytes: total items at 20, donation BP at 24,
    // enhancement at 28, enhancement expiration YYYYMMDD at 32, pole donation at 36, page count
    // at 40, then 40-byte entries from 44: item ID (+0), 1-based position (+4), owner name
    // (16 bytes, +8), type ID (+24), quality (+28), plus (+29), gem1 (+30), gem2 (+31),
    // item battle power (+32), donation (+36).
    public sealed class MsgWeaponsInfo : MsgBase<Client>
    {
        public const int HeaderSize = 44;
        public const int EntrySize = 40;

        public readonly record struct WeaponEntry(
            uint ItemID,
            uint Position,
            string OwnerName,
            uint TypeID,
            byte Quality,
            byte Plus,
            byte Gem1,
            byte Gem2,
            uint BattlePower,
            uint Donation);

        public ushort Action { get; set; }
        public uint BeginAt { get; set; }
        public uint EndAt { get; set; }
        public uint TotemType { get; set; }
        public uint TotalCount { get; set; }
        public uint BattlePower { get; set; }
        public uint Enhancement { get; set; }
        public uint EnhancementExpiration { get; set; }
        public uint Donation { get; set; }
        public List<WeaponEntry> Items { get; } = new List<WeaponEntry>();

        public MsgWeaponsInfo() => base.Type = PacketType.MsgWeaponsInfo;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = reader.ReadUInt16();          // 4
            reader.ReadUInt16();                        // 6
            this.BeginAt = reader.ReadUInt32();         // 8
            this.EndAt = reader.ReadUInt32();           // 12
            this.TotemType = reader.ReadUInt32();       // 16
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Action);                  // 4
            writer.Write((ushort)0);                    // 6
            writer.Write(this.BeginAt);                 // 8
            writer.Write(this.EndAt);                   // 12
            writer.Write(this.TotemType);               // 16
            writer.Write(this.TotalCount);              // 20
            writer.Write(this.BattlePower);             // 24
            writer.Write(this.Enhancement);             // 28
            writer.Write(this.EnhancementExpiration);   // 32
            writer.Write(this.Donation);                // 36
            writer.Write((uint)this.Items.Count);       // 40
            foreach (var item in this.Items)
            {
                writer.Write(item.ItemID);              // +0
                writer.Write(item.Position);            // +4
                writer.Write(item.OwnerName ?? string.Empty, 16); // +8
                writer.Write(item.TypeID);              // +24
                writer.Write(item.Quality);             // +28
                writer.Write(item.Plus);                // +29
                writer.Write(item.Gem1);                // +30
                writer.Write(item.Gem2);                // +31
                writer.Write(item.BattlePower);         // +32
                writer.Write(item.Donation);            // +36
            }
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Guilds.SendWeaponsInfoAsync(client, this);
    }
}
