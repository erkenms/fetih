namespace Comet.Game.Items
{
    using System.Collections.Generic;
    using Comet.Game.Database.Models;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // A player's booth in the market, set up at one of the booth flags. The flag stays: the
    // client draws the booth number over it, by NPC ID (ini/booth.ini). Only changed under
    // Vending's gate.
    public sealed class Booth
    {
        // Albetros (5517): flags are role 16; an open booth is role 14 with look 406, three
        // cells east of its flag (where the 5619 client asks for it), named after its owner.
        // Its ID is the flag's in the dynamic NPC range (a guess).
        public const byte FlagRole = 16;
        public const uint IdOffset = 100000;
        public const byte Role = 14;
        public const ushort Lookface = 406;
        public const int FlagOffsetX = 3;

        public sealed record Goods(DbItem Item, uint Price, bool ConquerPoints);

        public Booth(Client owner, Npc flag)
        {
            this.Owner = owner;
            this.Flag = flag;
        }

        public Client Owner { get; }
        public Npc Flag { get; }
        public uint ID => this.Flag.NpcID + IdOffset;
        public ushort X => (ushort)(this.Flag.X + FlagOffsetX);
        public ushort Y => this.Flag.Y;

        // By item ID
        public Dictionary<uint, Goods> Items { get; } = new Dictionary<uint, Goods>();

        // The owner's last vendor message, shown to players who come by later
        public MsgTalk Hawk { get; set; }

        public MsgNpcInfo Spawn() => new MsgNpcInfo(this.ID, this.X, this.Y, Lookface, Role, this.Owner.Character?.Name ?? "");
    }
}
