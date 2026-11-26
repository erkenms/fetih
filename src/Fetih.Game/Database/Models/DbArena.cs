namespace Fetih.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("arena")]
    public class DbArena
    {
        public uint CharacterID { get; set; }
        public uint Points { get; set; }
        public uint Day { get; set; }
        public uint WinsToday { get; set; }
        public uint LossesToday { get; set; }
        public uint WinsTotal { get; set; }
        public uint LossesTotal { get; set; }
        public uint HonorPoints { get; set; }
        public uint HonorTotal { get; set; }
    }
}
