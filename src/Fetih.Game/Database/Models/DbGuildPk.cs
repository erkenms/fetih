namespace Fetih.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("guild_pk_entry")]
    public class DbGuildPkEntry
    {
        public uint SyndicateID { get; set; }
        public string Name { get; set; }
        public byte Seed { get; set; }
        public long ContestPoints { get; set; }
        public byte Played { get; set; }
        public int Points { get; set; }
        public long Score { get; set; }
        public byte FinalRank { get; set; }
    }

    [Table("guild_pk_prize")]
    public class DbGuildPkPrize
    {
        public uint SyndicateID { get; set; }
        public byte Kind { get; set; }
        public byte Rank { get; set; }
        public DateTime Expires { get; set; }
        public bool Claimed { get; set; }
    }

    [Table("guild_pk_exploit")]
    public class DbGuildPkExploit
    {
        public uint CharacterID { get; set; }
        public uint Exploits { get; set; }
    }
}
