namespace Fetih.Game.Packets
{
    using System.Collections.Generic;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using Fetih.Network.Packets;

    // An NPC with life, which the client lets players attack: 4 ID, 8 shop/owner ID,
    // 12 max life, 16 life, 20 X, 22 Y, 24 lookface, 26 role (10 pole, 21 stake, 22 scarecrow,
    // 26 gate), 28 sort, 30..35 reserved, 36 packed name strings.
    public sealed class MsgNpcInfoEx : MsgBase<Client>
    {
        public Npc Npc { get; }
        public uint MaxLife { get; }
        public uint Life { get; }
        public ushort Lookface { get; }
        public string Name { get; }

        public MsgNpcInfoEx(Npc npc, uint life)
            : this(npc, npc.Life, life, npc.Lookface, null) { }

        public MsgNpcInfoEx(Npc npc, uint maxLife, uint life, ushort lookface, string name)
        {
            base.Type = PacketType.MsgNpcInfoEx;
            this.Npc = npc;
            this.MaxLife = maxLife;
            this.Life = life;
            this.Lookface = lookface;
            this.Name = name;
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Npc.NpcID);                                   // 4
            writer.Write(0u);                                               // 8
            writer.Write(this.MaxLife);                                     // 12
            writer.Write(this.Life);                                        // 16
            writer.Write(this.Npc.X);                                       // 20
            writer.Write(this.Npc.Y);                                       // 22
            writer.Write(this.Lookface);                                    // 24
            writer.Write((ushort)this.Npc.Role);                            // 26
            writer.Write((ushort)this.Npc.Role);                            // 28
            writer.Write((ushort)0);                                        // 30
            writer.Write(0u);                                               // 32
            writer.Write(string.IsNullOrEmpty(this.Name)                    // 36
                ? new List<string>()
                : new List<string> { this.Name });
            return writer.ToArray();
        }
    }
}
