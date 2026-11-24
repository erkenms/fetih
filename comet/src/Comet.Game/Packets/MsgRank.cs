namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Flowers;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 24-byte header + 56-byte entries (minimum 80 bytes when empty).
    // Client->server: mode 1 queries a page (rank type at 8, 0-based page at 14);
    // mode 2 queries the player's own flower ranks (sent on login and when opening the dialog).
    // Server->client: mode 1 returns up to 10 entries (place at +0, amount at +8, name at +40);
    // mode 2 returns one rank per flower kind (place at 24, character ID at 44);
    // mode 4 broadcasts another player's charm ID (charm at 8, character ID at 44);
    // mode 5 ends a mode-2 response and triggers the client to compute its best charm and open/refresh the dialog.
    public sealed class MsgRank : MsgBase<Client>
    {
        public const byte QueryMode = 1;
        public const byte OwnFlowerRankMode = 2;
        public const byte OtherRankMode = 4;
        public const byte OwnRankEndMode = 5;

        public byte Mode { get; set; }
        public uint Data { get; set; }
        public byte Page { get; set; }
        public List<FlowerEntry> Entries { get; set; } = new List<FlowerEntry>();

        public MsgRank() => base.Type = PacketType.MsgRank;

        public static MsgRank PageResult(uint rankType, byte page, List<FlowerEntry> entries) => new MsgRank
        {
            Mode = QueryMode,
            Data = rankType,
            Page = page,
            Entries = entries
        };

        public static MsgRank OwnRank(uint rankType, int place, uint amount, uint characterID, string name) => new MsgRank
        {
            Mode = OwnFlowerRankMode,
            Data = rankType,
            Entries = new List<FlowerEntry> { new FlowerEntry(characterID, name, amount, place) }
        };

        public static MsgRank OtherRank(uint characterID, uint charmID) => new MsgRank
        {
            Mode = OtherRankMode,
            Data = charmID,
            Entries = new List<FlowerEntry> { new FlowerEntry(characterID, string.Empty, 0, 0) }
        };

        public static MsgRank OwnRankEnd() => new MsgRank
        {
            Mode = OwnRankEndMode
        };

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Mode = reader.ReadByte();
            reader.ReadBytes(3);
            this.Data = reader.ReadUInt32();
            reader.ReadUInt16();
            this.Page = reader.ReadByte();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Mode);                // 4
            writer.Write(new byte[3]);              // 5..7
            writer.Write(this.Data);                // 8..11
            writer.Write((ushort)0);                // 12..13
            writer.Write(this.Page);                // 14
            writer.Write((byte)0);                  // 15
            writer.Write(this.Entries.Count);       // 16..19
            writer.Write(0u);                       // 20..23

            if (this.Entries.Count == 0)
            {
                writer.Write(new byte[56]);         // pad to 80 bytes
                return writer.ToArray();
            }

            foreach (var entry in this.Entries)
            {
                writer.Write((ulong)entry.Place);   // +0..+7   (24)
                writer.Write((ulong)entry.Amount);  // +8..+15  (32)
                writer.Write(entry.CharacterID);    // +16..+19 (40)
                writer.Write(entry.CharacterID);    // +20..+23 (44) read by modes 2 & 4
                writer.Write(entry.Name ?? string.Empty, 16); // +24..+39 (48)
                writer.Write(entry.Name ?? string.Empty, 16); // +40..+55 (64) read by mode 1
            }
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) =>
            FlowerRanking.HandleRankAsync(client, this);
    }
}
