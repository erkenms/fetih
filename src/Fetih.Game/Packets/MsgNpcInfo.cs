namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using Fetih.Game.World;
    using Fetih.Game.States;
    using Fetih.Network.Packets;

    public sealed class MsgNpcInfo : MsgBase<Client>
    {
        public uint NpcID { get; }
        public ushort X { get; }
        public ushort Y { get; }
        public ushort Lookface { get; }
        public ushort Role { get; }

        // Null for NPCs the client names itself; booths carry their owner's name
        public string Name { get; }

        public MsgNpcInfo(Npc npc) : this(npc.NpcID, npc.X, npc.Y, npc.Lookface, npc.Role, null) { }

        public MsgNpcInfo(uint npcID, ushort x, ushort y, ushort lookface, ushort role, string name)
        {
            base.Type = PacketType.MsgNpcInfo;
            this.NpcID = npcID;
            this.X = x;
            this.Y = y;
            this.Lookface = lookface;
            this.Role = role;
            this.Name = name;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.NpcID);               // 4
            writer.Write(this.NpcID);               // 8   static ID
            writer.Write(this.X);                   // 12
            writer.Write(this.Y);                   // 14
            writer.Write(this.Lookface);            // 16
            writer.Write(this.Role);                // 18
            writer.Write((ushort)0);                // 20  sort
            writer.Write(this.Name == null ? new List<string>() : new List<string> { this.Name }); // 22
            writer.Write((byte)0);
            return writer.ToArray();
        }
    }
}
