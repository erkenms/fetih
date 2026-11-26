namespace Fetih.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("guild_reward_pool")]
    public class DbGuildRewardPool
    {
        public uint SyndicateID { get; set; }
        public byte Event { get; set; }
        public uint ConquerPoints { get; set; }
        public ulong Silver { get; set; }
    }

    [Table("guild_reward_share")]
    public class DbGuildRewardShare
    {
        public uint SyndicateID { get; set; }
        public byte Event { get; set; }
        public uint CharacterID { get; set; }
        public string Name { get; set; }
        public uint Exploits { get; set; }
        public uint ConquerPoints { get; set; }
        public ulong Silver { get; set; }
    }
}
