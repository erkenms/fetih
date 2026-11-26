namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // 32 bytes: the other player's ID at 4, action byte at 8, online byte at 9,
    // minutes left of the trial at 10, the name (16 bytes) at 16
    public sealed class MsgTradeBuddy : MsgBase<Client>
    {
        public uint ID { get; set; }
        public PartnerAction Action { get; set; }
        public bool Online { get; set; }
        public int TrialMinutes { get; set; }
        public string Name { get; set; }

        public MsgTradeBuddy() => base.Type = PacketType.MsgTradeBuddy;

        public MsgTradeBuddy(PartnerAction action, uint id, string name, bool online, int trialMinutes = 0) : this()
        {
            this.Action = action;
            this.ID = id;
            this.Name = name;
            this.Online = online;
            this.TrialMinutes = trialMinutes;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.ID = reader.ReadUInt32();
            this.Action = (PartnerAction)reader.ReadByte();
            this.Online = reader.ReadByte() != 0;
            this.TrialMinutes = reader.ReadInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.ID);                          // 4
            writer.Write((byte)this.Action);                // 8
            writer.Write((byte)(this.Online ? 1 : 0));      // 9
            writer.Write(this.TrialMinutes);                // 10
            writer.Write((ushort)0);                        // 14
            writer.Write(this.Name ?? string.Empty, 16);    // 16
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => TradePartners.HandleAsync(client, this);

        public enum PartnerAction : byte
        {
            // From the client with the other player's ID; forwarded to them with the asker's.
            // Asking back accepts.
            Request = 0,
            Reject = 1,
            // Never sent; they keep Break at 4
            Online = 2,
            Offline = 3,
            Break = 4,
            // A partner in the list
            Add = 5,
        }
    }
}
