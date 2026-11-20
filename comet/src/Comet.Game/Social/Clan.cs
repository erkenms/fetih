namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    public enum ClanRank : ushort
    {
        None = 0,
        Member = 10,
        Spouse = 11,
        Leader = 100,
    }

    public sealed class ClanMember
    {
        public uint CharacterID { get; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public ushort Class { get; set; }
        public uint Mesh { get; set; }
        public ClanRank Rank { get; set; }
        public ulong Donation { get; set; }
        public uint Joined { get; }

        public ClanMember(uint characterID, string name, byte level, ClanRank rank, uint joined)
        {
            this.CharacterID = characterID;
            this.Name = name;
            this.Level = level;
            this.Rank = rank;
            this.Joined = joined;
        }

        public DbFamilyMember ToRow(uint clanID) => new DbFamilyMember
        {
            CharacterID = this.CharacterID,
            FamilyID = clanID,
            Rank = (ushort)this.Rank,
            Donation = this.Donation,
            Joined = this.Joined,
        };
    }

    // Changed only under Clans' gate; MsgPlayer reads a member's clan and rank from other
    // queues, which a torn read can't break
    public sealed class Clan
    {
        public const int MaxMembers = 6;
        public const int MaxAllies = 5, MaxEnemies = 5;

        public uint ID { get; }
        public string Name { get; }
        public uint LeaderID { get; set; }
        public ulong Fund { get; set; }
        public byte Level { get; set; } = 1;
        public string Announcement { get; set; } = string.Empty;
        public uint Created { get; set; }

        public Dictionary<uint, ClanMember> Members { get; } = new Dictionary<uint, ClanMember>();
        public HashSet<uint> Allies { get; } = new HashSet<uint>();
        public HashSet<uint> Enemies { get; } = new HashSet<uint>();

        public Clan(uint id, string name)
        {
            this.ID = id;
            this.Name = name;
        }

        public ClanMember Leader => this.Members.GetValueOrDefault(this.LeaderID);

        public bool Full => this.Members.Count >= MaxMembers;

        public ClanMember Find(string name) =>
            this.Members.Values.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

        public DbFamily ToRow() => new DbFamily
        {
            FamilyID = this.ID,
            Name = this.Name,
            LeaderID = this.LeaderID,
            Fund = this.Fund,
            Level = this.Level,
            Announcement = this.Announcement,
            Created = this.Created,
        };

        public static Clan FromRow(DbFamily row) => new Clan(row.FamilyID, row.Name)
        {
            LeaderID = row.LeaderID,
            Fund = row.Fund,
            Level = row.Level,
            Announcement = row.Announcement ?? string.Empty,
            Created = row.Created,
        };
    }
}
