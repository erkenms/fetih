namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("elite_pk_reward")]
    public class DbElitePkReward
    {
        public uint CharacterID { get; set; }
        public byte Group { get; set; }
        public byte Rank { get; set; }
        public string Name { get; set; }
        public bool Claimed { get; set; }
    }
}
