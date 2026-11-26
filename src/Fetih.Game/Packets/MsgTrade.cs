namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.Social;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // A player ID, item ID or amount at 4, the action at 8
    public sealed class MsgTrade : MsgBase<Client>
    {
        public uint Data { get; set; }
        public TradeAction Action { get; set; }

        public MsgTrade() => base.Type = PacketType.MsgTrade;

        public MsgTrade(TradeAction action, uint data) : this()
        {
            this.Action = action;
            this.Data = data;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Data = reader.ReadUInt32();
            this.Action = (TradeAction)reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Data);                // 4
            writer.Write((uint)this.Action);        // 8
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Trading.HandleAsync(client, this);

        public enum TradeAction : ushort
        {
            // From the client with the other player's ID; to the other player with the asker's
            Request = 1,
            Close = 2,
            // Opens the window, with the partner's ID
            ShowTable = 3,
            HideTable = 5,
            AddItem = 6,
            SetSilver = 7,
            // The partner's silver offer
            ShowSilver = 8,
            Accept = 10,
            AddItemFailed = 11,
            ShowConquerPoints = 12,
            SetConquerPoints = 13,
            TimeOut = 17,
        }
    }
}
