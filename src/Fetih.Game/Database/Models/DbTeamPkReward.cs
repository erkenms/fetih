namespace Fetih.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("team_pk_reward")]
    public class DbTeamPkReward
    {
        public byte Event { get; set; }
        public uint LeaderID { get; set; }
        public byte Group { get; set; }
        public byte Rank { get; set; }
        public string Name { get; set; }
        public bool Claimed { get; set; }
    }
}
