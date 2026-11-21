namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Nobility;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Nobility. Action u16 at 4, data from 8. Client: donation u64 at 8 with the currency
    // u16 at 16 (0 silver, 1 CPs, 2 bound CPs), board page u32 at 8, rank u32 at 8 to ask the
    // minimum for it.
    // Server: donation and rank strings at 32 ("id 0 amount total position" and "id total
    // rank position", positions from 0, -1 off the board); the board's page u16 at 8, pages
    // u16 at 10, count at 12 and 48-byte entries from 32 (ID, online byte at 4, look at 8,
    // name (16 bytes) at 12, donation u64 at 32, rank at 40, position at 44); a minimum as
    // the silver still needed u64 at 8, the position it would give at 20 and the current
    // one at 24.
    public sealed class MsgPeerage : MsgBase<Client>
    {
        public const int EntrySize = 48;

        public PeerageAction Action { get; set; }
        public ulong Data { get; set; }
        public ushort Currency { get; set; }
        public int Position { get; set; }
        public int CurrentPosition { get; set; }
        public ushort Pages { get; set; }
        public List<string> Strings { get; set; }
        public List<(Donor Donor, bool Online, Standing Standing)> Entries { get; set; }

        public MsgPeerage() => base.Type = PacketType.MsgPeerage;

        public static MsgPeerage Donated(uint id, ulong amount, ulong total, int position) =>
            new MsgPeerage
            {
                Action = PeerageAction.Donate,
                Strings = new List<string> { $"{id} 0 {amount} {total} {position}" },
            };

        public static MsgPeerage Info(uint id, ulong total, Standing standing) =>
            new MsgPeerage
            {
                Action = PeerageAction.Info,
                Data = id,
                Strings = new List<string> { $"{id} {total} {(uint)standing.Rank} {standing.Position}" },
            };

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (PeerageAction)reader.ReadUInt16();
            if (bytes.Length < 16)
                return;
            reader.ReadUInt16();
            this.Data = reader.ReadUInt64();
            if (bytes.Length >= 18)
                this.Currency = reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);          // 4
            writer.Write((ushort)0);                    // 6
            if (this.Entries != null)
            {
                writer.Write((ushort)this.Data);        // 8  page
                writer.Write(this.Pages);               // 10
                writer.Write(this.Entries.Count);       // 12
                writer.Write(new byte[16]);             // 16
                foreach (var (donor, online, standing) in this.Entries)
                {
                    writer.Write(donor.ID);
                    writer.Write((byte)(online ? 1 : 0));
                    writer.Write(new byte[3]);
                    writer.Write(donor.Mesh);
                    writer.Write(donor.Name, 16);
                    writer.Write(0u);
                    writer.Write(donor.Donation);
                    writer.Write((uint)standing.Rank);
                    writer.Write(standing.Position);
                }
                return writer.ToArray();
            }
            writer.Write(this.Data);                    // 8
            writer.Write(0u);                           // 16
            writer.Write(this.Position);                // 20
            writer.Write(this.CurrentPosition);         // 24
            writer.Write(0u);                           // 28
            if (this.Strings != null)
                writer.Write(this.Strings);             // 32
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Peerage.HandleAsync(client, this);

        public enum PeerageAction : ushort
        {
            Donate = 1,
            Board = 2,
            Info = 3,
            Minimum = 4,
        }
    }
}
