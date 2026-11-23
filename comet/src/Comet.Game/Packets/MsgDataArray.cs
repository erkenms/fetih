namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Items;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // An action byte at 4, the count of values byte at 5, then the values (uint each) from 8.
    // Window 1's compose window sends action 0 with the main item and one minor item; the
    // Wuxing Oven opens the one that sends MsgFuse. The client doesn't process it, so it
    // isn't answered; the items' updates are.
    public sealed class MsgDataArray : MsgBase<Client>
    {
        public DataArrayAction Action { get; set; }
        public uint[] Values { get; set; } = new uint[0];

        public MsgDataArray() => base.Type = PacketType.MsgDataArray;

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (DataArrayAction)reader.ReadByte();
            int count = reader.ReadByte();
            reader.ReadUInt16();
            int available = (bytes.Length - 8) / 4;
            this.Values = new uint[System.Math.Min(count, available)];
            for (int i = 0; i < this.Values.Length; i++)
                this.Values[i] = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((byte)this.Action);
            writer.Write((byte)this.Values.Length);
            writer.Write((ushort)0);
            foreach (var value in this.Values)
                writer.Write(value);
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => this.Action switch
        {
            DataArrayAction.Compose when this.Values.Length >= 2 =>
                Composing.ComposeAsync(client, this.Values[0], this.Values[1]),
            _ => Composing.UnsupportedAsync(client, this),
        };

        public enum DataArrayAction : byte
        {
            Compose = 0,
        }
    }
}
