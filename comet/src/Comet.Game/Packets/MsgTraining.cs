namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Game.Training;
    using Comet.Network.Packets;

    // Offline training (5619 client's CMsgTraining), 12 bytes: action u16 at 4, data u32 at 8.
    // Client: 0 asks before opening the dialog (also when blessed, seen in the VM), 1 enters,
    // 3 asks for the result, 4 claims it. Server: 0 the minutes for the dialog, 1 shows the
    // dialog that follows entering, 2 makes the client ask for the result (3).
    public sealed class MsgTraining : MsgBase<Client>
    {
        public TrainingAction Action { get; set; }
        public uint Data { get; set; }

        public MsgTraining() => base.Type = PacketType.MsgTraining;

        public MsgTraining(TrainingAction action, uint data = 0) : this()
        {
            this.Action = action;
            this.Data = data;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (TrainingAction)reader.ReadUInt16();
            if (bytes.Length >= 12)
            {
                reader.ReadUInt16();
                this.Data = reader.ReadUInt32();
            }
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((ushort)this.Action);  // 4
            writer.Write((ushort)0);
            writer.Write(this.Data);            // 8
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => OfflineTraining.HandleAsync(client, this);

        public enum TrainingAction : ushort
        {
            Query = 0,
            Enter = 1,
            AskResult = 2,
            Result = 3,
            Claim = 4,
        }
    }

    // The offline training result (5619 client's CMsgTrainingInfo), 16 bytes: minutes trained
    // u16 at 4, minutes left u16 at 6, then the level after claiming u16 at 8 and the
    // progress into it in ten-millionths u32 at 12 ("%.5lf => %.5lf" in the dialog)
    public sealed class MsgTrainingInfo : MsgBase<Client>
    {
        public ushort Trained { get; set; }
        public ushort Left { get; set; }
        public ushort Level { get; set; }
        public uint Progress { get; set; }

        public MsgTrainingInfo() => base.Type = PacketType.MsgTrainingInfo;

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Trained);         // 4
            writer.Write(this.Left);            // 6
            writer.Write(this.Level);           // 8
            writer.Write((ushort)0);
            writer.Write(this.Progress);        // 12
            return writer.ToArray();
        }
    }
}
