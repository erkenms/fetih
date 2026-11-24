namespace Comet.Game.Database.Models
{
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("family")]
    public class DbFamily
    {
        public uint FamilyID { get; set; }
        public string Name { get; set; }
        public uint LeaderID { get; set; }
        public ulong Fund { get; set; }
        public byte Level { get; set; }
        public string Announcement { get; set; }
        public uint Created { get; set; }
    }

    [Table("family_member")]
    public class DbFamilyMember
    {
        public uint CharacterID { get; set; }
        public uint FamilyID { get; set; }
        public ushort Rank { get; set; }
        public ulong Donation { get; set; }
        public uint Joined { get; set; }
    }

    // Kind 1 ally, 2 enemy
    [Table("family_relation")]
    public class DbFamilyRelation
    {
        public uint FamilyID { get; set; }
        public uint TargetID { get; set; }
        public byte Kind { get; set; }
    }

    [Table("family_occupy")]
    public class DbFamilyOccupy
    {
        public uint NpcID { get; set; }
        public uint FamilyID { get; set; }
        public uint Days { get; set; }
        public bool RevenueClaimed { get; set; }
    }

    [Table("family_war_signup")]
    public class DbFamilyWarSignup
    {
        public uint FamilyID { get; set; }
        public uint NpcID { get; set; }
    }
}
