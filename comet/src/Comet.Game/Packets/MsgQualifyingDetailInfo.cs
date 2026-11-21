namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Arena;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The player's own arena details. 44 bytes, as the 5619 client asks with it: rank at 4,
    // activity points at 8 (left 0), status at 12 (0 not joined, 1 waiting, 2 waiting
    // inactive, 3 in combat), total wins at 16, total losses at 20, wins today at 24,
    // losses today at 28, total honor at 32, current honor at 36, arena points at 40.
    public sealed class MsgQualifyingDetailInfo : MsgBase<Client>
    {
        public const int Size = 44;

        public Fighter Fighter { get; set; }
        public QualifierStatus Status { get; set; }

        public MsgQualifyingDetailInfo() => base.Type = PacketType.MsgQualifyingDetailInfo;

        public MsgQualifyingDetailInfo(Fighter fighter, QualifierStatus status) : this()
        {
            this.Fighter = fighter;
            this.Status = status;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var fighter = this.Fighter;
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(fighter?.Rank ?? 0);           // 4
            writer.Write(0u);                           // 8
            writer.Write((uint)this.Status);            // 12
            writer.Write(fighter?.WinsTotal ?? 0);      // 16
            writer.Write(fighter?.LossesTotal ?? 0);    // 20
            writer.Write(fighter?.WinsToday ?? 0);      // 24
            writer.Write(fighter?.LossesToday ?? 0);    // 28
            writer.Write(fighter?.HonorTotal ?? 0);     // 32
            writer.Write(fighter?.HonorPoints ?? 0);    // 36
            writer.Write(fighter?.Points ?? 0);         // 40
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Qualifier.SendDetailsAsync(client);
    }
}
