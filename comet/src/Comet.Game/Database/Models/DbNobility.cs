namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("nobility")]
    public class DbNobility
    {
        public uint CharacterID { get; set; }
        public ulong Donation { get; set; }
    }
}
