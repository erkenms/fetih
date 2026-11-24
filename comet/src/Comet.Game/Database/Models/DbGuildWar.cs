namespace Comet.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("guild_war")]
    public class DbGuildWar
    {
        public uint ID { get; set; } = 1;
        public uint WinnerGuildID { get; set; }
        public uint HolderGuildID { get; set; }
        public bool RewardClaimed { get; set; }
        public DateTime? WonAt { get; set; }
    }
}
