namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("syndicate")]
    public class DbSyndicate
    {
        public uint SyndicateID { get; set; }
        public string Name { get; set; }
        public uint LeaderID { get; set; }
        public ulong Fund { get; set; }
        public uint ConquerPointsFund { get; set; }
        public string Announcement { get; set; }
        public uint AnnouncementDate { get; set; }
        public byte Level { get; set; }
        public uint RequiredLevel { get; set; }
        public uint RequiredRebirths { get; set; }
        public uint RequiredProfessions { get; set; }
        public uint Created { get; set; }
    }

    [Table("syndicate_member")]
    public class DbSyndicateMember
    {
        public uint CharacterID { get; set; }
        public uint SyndicateID { get; set; }
        public ushort Rank { get; set; }
        public uint SilverDonation { get; set; }
        public uint ConquerPointsDonation { get; set; }
        public uint Joined { get; set; }
    }

    // Kind 1 ally, 2 enemy
    [Table("syndicate_relation")]
    public class DbSyndicateRelation
    {
        public uint SyndicateID { get; set; }
        public uint TargetID { get; set; }
        public byte Kind { get; set; }
    }
}
