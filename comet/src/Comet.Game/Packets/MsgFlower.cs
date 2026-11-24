namespace Comet.Game.Packets
{
    using System;
    using System.Threading.Tasks;
    using Comet.Game.Flowers;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // 60 bytes: mode at 4, target ID at 8, item ID at 12 (0 for the daily free flower).
    // Server->client mode 0 carries sender name (16..31), receiver name (32..47), amount (48),
    // flower kind (52), and free-flower flag bit 0 at 56 (unregisters the sender's daily icon).
    // Server->client mode 2 carries (total, today) pairs for RedRose, Lily, Orchid, Tulip at
    // 16..47 for female players, or triggers the daily free-flower icon for male players.
    public sealed class MsgFlower : MsgBase<Client>
    {
        public const ushort SendFlowerMode = 0;
        public const ushort InfoMode = 2;

        public ushort Mode { get; set; }
        public uint TargetID { get; set; }
        public uint ItemID { get; set; }

        public string SenderName { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public uint Amount { get; set; }
        public FlowerKind Kind { get; set; }
        public uint Flags { get; set; }

        public uint RedRoses { get; set; }
        public uint RedRosesToday { get; set; }
        public uint Lilies { get; set; }
        public uint LiliesToday { get; set; }
        public uint Orchids { get; set; }
        public uint OrchidsToday { get; set; }
        public uint Tulips { get; set; }
        public uint TulipsToday { get; set; }

        public MsgFlower() => base.Type = PacketType.MsgFlower;

        public static MsgFlower DailyAvailable(uint characterID) => new MsgFlower
        {
            Mode = InfoMode,
            TargetID = characterID
        };

        public static MsgFlower Info(uint characterID, FlowerRecord record, DateTime today) => new MsgFlower
        {
            Mode = InfoMode,
            TargetID = characterID,
            RedRoses = record.RedRoses,
            RedRosesToday = record.TodayOf(FlowerKind.RedRose, today),
            Lilies = record.Lilies,
            LiliesToday = record.TodayOf(FlowerKind.Lily, today),
            Orchids = record.Orchids,
            OrchidsToday = record.TodayOf(FlowerKind.Orchid, today),
            Tulips = record.Tulips,
            TulipsToday = record.TodayOf(FlowerKind.Tulip, today)
        };

        public static MsgFlower Sent(string senderName, string receiverName, uint amount, FlowerKind kind, bool isFree) =>
            new MsgFlower
            {
                Mode = SendFlowerMode,
                SenderName = senderName,
                ReceiverName = receiverName,
                Amount = amount,
                Kind = kind,
                Flags = isFree ? 1u : 0u
            };

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Mode = reader.ReadUInt16();
            reader.ReadUInt16();
            this.TargetID = reader.ReadUInt32();
            this.ItemID = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Mode);          // 4
            writer.Write(this.TargetID);            // 8
            writer.Write(this.ItemID);              // 12
            if (this.Mode == SendFlowerMode)
            {
                writer.Write(this.SenderName, 16);   // 16
                writer.Write(this.ReceiverName, 16); // 32
                writer.Write(this.Amount);           // 48
                writer.Write((uint)this.Kind);       // 52
                writer.Write(this.Flags);            // 56
            }
            else
            {
                writer.Write(this.RedRoses);         // 16
                writer.Write(this.RedRosesToday);    // 20
                writer.Write(this.Lilies);           // 24
                writer.Write(this.LiliesToday);      // 28
                writer.Write(this.Orchids);          // 32
                writer.Write(this.OrchidsToday);     // 36
                writer.Write(this.Tulips);           // 40
                writer.Write(this.TulipsToday);      // 44
                writer.Write(new byte[12]);          // 48..59
            }
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) =>
            FlowerRanking.HandleSendAsync(client, this);
    }
}
