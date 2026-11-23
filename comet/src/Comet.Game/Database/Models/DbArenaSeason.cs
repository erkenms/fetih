namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("arena_season")]
    public class DbArenaSeason
    {
        public uint Season { get; set; }
        public uint Rank { get; set; }
        public uint CharacterID { get; set; }
        public string Name { get; set; }
        public uint Mesh { get; set; }
        public byte Level { get; set; }
        public byte Class { get; set; }
        public uint Points { get; set; }
        public uint Wins { get; set; }
        public uint Losses { get; set; }
    }
}
