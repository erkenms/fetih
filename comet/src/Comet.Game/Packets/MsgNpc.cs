namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Npcs;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The player clicked an NPC
    public sealed class MsgNpc : MsgBase<Client>
    {
        public uint NpcID { get; set; }
        public uint Data { get; set; }
        public ushort Event { get; set; }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.NpcID = reader.ReadUInt32();
            this.Data = reader.ReadUInt32();
            this.Event = reader.ReadUInt16();
        }

        public override Task ProcessAsync(Client client)
        {
            if (this.Event == 0)
                return Dialogs.StartAsync(client, this.NpcID);
            LogEvents.For<MsgNpc>(LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} event {Event} ({Length} bytes)", this.Type, this.Event, this.Length);
            return Task.CompletedTask;
        }
    }
}
