namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The player adds attribute points from the status window: one byte each for
    // strength, agility, vitality and spirit
    public sealed class MsgAllot : MsgBase<Client>
    {
        public byte Strength { get; set; }
        public byte Agility { get; set; }
        public byte Vitality { get; set; }
        public byte Spirit { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Strength = reader.ReadByte();
            this.Agility = reader.ReadByte();
            this.Vitality = reader.ReadByte();
            this.Spirit = reader.ReadByte();
        }

        public override Task ProcessAsync(Client client) =>
            Progress.AllotAsync(client, this.Strength, this.Agility, this.Vitality, this.Spirit);
    }
}
