namespace Comet.Game.Packets
{
    using System.Collections.Generic;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    // An NPC with life, which the client lets players attack: 4 ID, 8 unused, 12 max life,
    // 16 life, 20 X, 22 Y, 24 lookface, 26 role, 28 role again, 30 names (none here).
    // Not yet seen in the 5619 client.
    public sealed class MsgNpcInfoEx : MsgBase<Client>
    {
        public Npc Npc { get; }
        public uint Life { get; }

        public MsgNpcInfoEx(Npc npc, uint life)
        {
            base.Type = PacketType.MsgNpcInfoEx;
            this.Npc = npc;
            this.Life = life;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Npc.NpcID);           // 4
            writer.Write(0u);                       // 8
            writer.Write(this.Npc.Life);            // 12  max life
            writer.Write(this.Life);                // 16
            writer.Write(this.Npc.X);               // 20
            writer.Write(this.Npc.Y);               // 22
            writer.Write(this.Npc.Lookface);        // 24
            writer.Write((ushort)this.Npc.Role);    // 26
            writer.Write((ushort)this.Npc.Role);    // 28
            writer.Write(new List<string>());       // 30
            return writer.ToArray();
        }
    }
}
