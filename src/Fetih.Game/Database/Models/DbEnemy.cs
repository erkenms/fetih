namespace Fetih.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("enemy")]
    public class DbEnemy
    {
        public uint CharacterID { get; set; }
        public uint EnemyID { get; set; }
        // When they last killed the character; the oldest gives way past the limit
        public DateTime AddedAt { get; set; }
    }
}
