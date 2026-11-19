namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("level_exp")]
    public class DbLevelExp
    {
        public byte Level { get; set; }
        public ulong Experience { get; set; }
    }
}
