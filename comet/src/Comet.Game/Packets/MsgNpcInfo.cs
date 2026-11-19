namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.World;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // An NPC standing on the map
    public sealed class MsgNpcInfo : MsgBase<Client>
    {
        public Npc Npc { get; }

        public MsgNpcInfo(Npc npc)
        {
            base.Type = PacketType.MsgNpcInfo;
            this.Npc = npc;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Npc.NpcID);           // 4
            writer.Write(this.Npc.NpcID);           // 8   static ID
            writer.Write(this.Npc.X);               // 12
            writer.Write(this.Npc.Y);               // 14
            writer.Write(this.Npc.Lookface);        // 16
            writer.Write((ushort)this.Npc.Role);    // 18
            writer.Write((ushort)0);                // 20  sort
            writer.Write(new List<string>());       // 22  no name: the client has it
            writer.Write((byte)0);
            return writer.ToArray();
        }
    }
}
