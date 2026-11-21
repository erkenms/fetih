namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class GuildsRepository
    {
        public sealed record MemberRow(DbSyndicateMember Member, string Name, byte Level);

        public static async Task<(List<DbSyndicate> Guilds, List<MemberRow> Members, List<DbSyndicateRelation> Relations)>
            LoadAsync()
        {
            using var db = new ServerDbContext();
            var guilds = await db.Syndicates.ToListAsync();
            var members = await db.SyndicateMembers
                .Join(db.Characters, m => m.CharacterID, c => c.CharacterID, (m, c) => new { m, c.Name, c.Level })
                .ToListAsync();
            var relations = await db.SyndicateRelations.ToListAsync();
            return (guilds, members.Select(x => new MemberRow(x.m, x.Name, x.Level)).ToList(), relations);
        }

        public static async Task<uint> CreateAsync(DbSyndicate guild, DbSyndicateMember leader)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            db.Syndicates.Add(guild);
            await db.SaveChangesAsync();
            leader.SyndicateID = guild.SyndicateID;
            db.SyndicateMembers.Add(leader);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return guild.SyndicateID;
        }

        public static async Task SaveAsync(DbSyndicate guild)
        {
            using var db = new ServerDbContext();
            db.Syndicates.Update(guild);
            await db.SaveChangesAsync();
        }

        public static async Task DeleteAsync(uint guildID)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            await db.SyndicateMembers.Where(m => m.SyndicateID == guildID).ExecuteDeleteAsync();
            await db.SyndicateRelations.Where(r => r.SyndicateID == guildID || r.TargetID == guildID).ExecuteDeleteAsync();
            await db.Syndicates.Where(s => s.SyndicateID == guildID).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        public static async Task SaveMemberAsync(DbSyndicateMember member)
        {
            using var db = new ServerDbContext();
            if (await db.SyndicateMembers.AnyAsync(m => m.CharacterID == member.CharacterID))
                db.SyndicateMembers.Update(member);
            else
                db.SyndicateMembers.Add(member);
            await db.SaveChangesAsync();
        }

        public static async Task RemoveMemberAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            await db.SyndicateMembers.Where(m => m.CharacterID == characterID).ExecuteDeleteAsync();
        }

        public static async Task SetRelationAsync(uint guildID, uint targetID, byte kind)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO syndicate_relation (SyndicateID, TargetID, Kind) VALUES ({guildID}, {targetID}, {kind}) ON DUPLICATE KEY UPDATE Kind = VALUES(Kind)");
        }

        public static async Task RemoveRelationAsync(uint guildID, uint targetID)
        {
            using var db = new ServerDbContext();
            await db.SyndicateRelations.Where(r => r.SyndicateID == guildID && r.TargetID == targetID)
                .ExecuteDeleteAsync();
        }
    }
}
