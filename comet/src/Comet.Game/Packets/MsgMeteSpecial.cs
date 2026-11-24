namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // Reincarnation panel (dialog 485): 12 bytes.
    // C->S: [4] int Profession (11, 21, 41, 51, 61, 71, 132, 142),
    //       [8] int Body (1=AgileFemale, 2=MuscularFemale, 3=AgileMale, 4=MuscularMale).
    // S->C: [4] int Result (1=Success, 2=Dead, 3=NotMaxRank, 4=NotSecondReborn, 5=LevelLow, 6=NoItem).
    public sealed class MsgMeteSpecial : MsgBase<Client>
    {
        public const int Size = 12;

        public int Profession { get; set; }
        public int Body { get; set; }

        public MsgMeteSpecial()
        {
            this.Type = PacketType.MsgMeteSpecial;
        }

        public MsgMeteSpecial(Result result) : this()
        {
            this.Profession = (int)result;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Profession = reader.ReadInt32();
            this.Body = reader.ReadInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)this.Type);
            writer.Write(this.Profession);
            writer.Write(this.Body);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) =>
            Rebirth.ReincarnateAsync(client, (byte)this.Profession, this.Body);

        public enum Result
        {
            Success = 1,
            Dead = 2,
            NotMaxRank = 3,
            NotSecondReborn = 4,
            LevelLow = 5,
            NoItem = 6,
        }
    }
}
