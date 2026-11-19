namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("npc")]
    public class DbNpc
    {
        public uint NpcID { get; set; }
        public ushort Lookface { get; set; }
        public byte Role { get; set; }
        public uint MapID { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
    }
}
