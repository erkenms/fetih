namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("enemy")]
    public class DbEnemy
    {
        public uint CharacterID { get; set; }
        public uint EnemyID { get; set; }
    }
}
