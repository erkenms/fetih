namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    public enum GuildRank : ushort
    {
        None = 0,
        Member = 200,
        DeputyLeader = 990,
        Leader = 1000,
    }

    public sealed class GuildMember
    {
        public uint CharacterID { get; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public GuildRank Rank { get; set; }
        public uint SilverDonation { get; set; }
        public uint ConquerPointsDonation { get; set; }
        public uint ArsenalDonation { get; set; }
        public uint Joined { get; }

        // Sum of tracked guild donation columns (silver + CPs + arsenal; guide and PK are not tracked yet)
        public uint TotalDonation =>
            (uint)Math.Min((ulong)this.SilverDonation + this.ConquerPointsDonation + this.ArsenalDonation, uint.MaxValue);

        public GuildMember(uint characterID, string name, byte level, GuildRank rank, uint joined)
        {
            this.CharacterID = characterID;
            this.Name = name;
            this.Level = level;
            this.Rank = rank;
            this.Joined = joined;
        }

        public DbSyndicateMember ToRow(uint guildID) => new DbSyndicateMember
        {
            CharacterID = this.CharacterID,
            SyndicateID = guildID,
            Rank = (ushort)this.Rank,
            SilverDonation = this.SilverDonation,
            ConquerPointsDonation = this.ConquerPointsDonation,
            Joined = this.Joined,
        };
    }

    // Changed only under Guilds' gate; MsgPlayer reads a member's guild and rank from other
    // queues, which a torn read can't break
    public sealed class Guild
    {
        public const int MaxAllies = 5, MaxEnemies = 5, MaxDeputies = 5;
        public const byte RelationAlly = 1, RelationEnemy = 2;

        // Bits of the profession requirement: classes the guild turns away, in the order of
        // the client's checkboxes
        public static uint ProfessionBit(ushort currentClass) => (currentClass / 10) switch
        {
            1 => 0x01,
            2 => 0x02,
            4 => 0x08,
            5 => 0x10,
            6 => 0x20,
            7 => 0x40,
            _ => currentClass >= 100 ? 0x04u : 0u,
        };

        public uint ID { get; }
        public string Name { get; }
        public uint LeaderID { get; set; }
        public ulong Fund { get; set; }
        public uint ConquerPointsFund { get; set; }
        public string Announcement { get; set; } = string.Empty;
        public uint AnnouncementDate { get; set; }
        public byte Level { get; set; } = 1;
        public uint RequiredLevel { get; set; } = 1;
        public uint RequiredRebirths { get; set; }
        public uint RequiredProfessions { get; set; }
        public uint Created { get; set; }
        public uint LastOpenTotem { get; set; }

        public Dictionary<uint, GuildMember> Members { get; } = new Dictionary<uint, GuildMember>();
        public HashSet<uint> Allies { get; } = new HashSet<uint>();
        public HashSet<uint> Enemies { get; } = new HashSet<uint>();
        public GuildArsenal Arsenal { get; } = new GuildArsenal();

        public Guild(uint id, string name)
        {
            this.ID = id;
            this.Name = name;
        }

        public GuildMember Leader => this.Members.GetValueOrDefault(this.LeaderID);

        public int Deputies => this.Members.Values.Count(m => m.Rank == GuildRank.DeputyLeader);

        public GuildMember Find(string name) =>
            this.Members.Values.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

        // As the client reads a guild's name, and its allies and enemies: name, leader,
        // level and member count
        public string Summary => $"{this.Name} {this.Leader?.Name ?? "None"} {this.Level} {this.Members.Count}";

        public DbSyndicate ToRow() => new DbSyndicate
        {
            SyndicateID = this.ID,
            Name = this.Name,
            LeaderID = this.LeaderID,
            Fund = this.Fund,
            ConquerPointsFund = this.ConquerPointsFund,
            Announcement = this.Announcement,
            AnnouncementDate = this.AnnouncementDate,
            Level = this.Level,
            RequiredLevel = this.RequiredLevel,
            RequiredRebirths = this.RequiredRebirths,
            RequiredProfessions = this.RequiredProfessions,
            Created = this.Created,
            LastOpenTotem = this.LastOpenTotem,
        };

        public static Guild FromRow(DbSyndicate row) => new Guild(row.SyndicateID, row.Name)
        {
            LeaderID = row.LeaderID,
            Fund = row.Fund,
            ConquerPointsFund = row.ConquerPointsFund,
            Announcement = row.Announcement ?? string.Empty,
            AnnouncementDate = row.AnnouncementDate,
            Level = row.Level,
            RequiredLevel = row.RequiredLevel,
            RequiredRebirths = row.RequiredRebirths,
            RequiredProfessions = row.RequiredProfessions,
            Created = row.Created,
            LastOpenTotem = row.LastOpenTotem,
        };

        public static uint Today => uint.Parse(Clock.Local(DateTime.UtcNow).ToString("yyyyMMdd"));
    }
}
