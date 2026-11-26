namespace Fetih.Game.Packets
{
    using System.Threading.Tasks;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    // The player adds attribute points from the status window. 24 bytes (VM-seen): 4 unknown
    // (0), then u32 each for strength at 8, agility at 12, vitality at 16 and spirit at 20.
    public sealed class MsgAllot : MsgBase<Client>
    {
        public uint Strength { get; set; }
        public uint Agility { get; set; }
        public uint Vitality { get; set; }
        public uint Spirit { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            reader.ReadUInt32();
            this.Strength = reader.ReadUInt32();
            this.Agility = reader.ReadUInt32();
            this.Vitality = reader.ReadUInt32();
            this.Spirit = reader.ReadUInt32();
        }

        public override Task ProcessAsync(Client client) =>
            Progress.AllotAsync(client, this.Strength, this.Agility, this.Vitality, this.Spirit);
    }
}
