namespace Fetih.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ClansRepository
    {
        public sealed record MemberRow(DbFamilyMember Member, string Name, byte Level, ushort Class, uint Mesh);

        public static async Task<(List<DbFamily> Clans, List<MemberRow> Members, List<DbFamilyRelation> Relations)>
            LoadAsync()
        {
            using var db = new ServerDbContext();
            var clans = await db.Families.ToListAsync();
            var members = await db.FamilyMembers
                .Join(db.Characters, m => m.CharacterID, c => c.CharacterID, (m, c) => new { m, c.Name, c.Level, c.CurrentClass, c.Mesh, c.Avatar })
                .ToListAsync();
            var relations = await db.FamilyRelations.ToListAsync();
            return (clans, members.Select(x => new MemberRow(x.m, x.Name, x.Level, x.CurrentClass, x.Mesh + x.Avatar * 10000u)).ToList(), relations);
        }

        public static async Task<uint> CreateAsync(DbFamily clan, DbFamilyMember leader)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            db.Families.Add(clan);
            await db.SaveChangesAsync();
            leader.FamilyID = clan.FamilyID;
            db.FamilyMembers.Add(leader);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return clan.FamilyID;
        }

        public static async Task SaveAsync(DbFamily clan)
        {
            using var db = new ServerDbContext();
            db.Families.Update(clan);
            await db.SaveChangesAsync();
        }

        public static async Task DeleteAsync(uint clanID)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            await db.FamilyMembers.Where(m => m.FamilyID == clanID).ExecuteDeleteAsync();
            await db.FamilyRelations.Where(r => r.FamilyID == clanID || r.TargetID == clanID).ExecuteDeleteAsync();
            await db.Families.Where(s => s.FamilyID == clanID).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        public static async Task SaveMemberAsync(DbFamilyMember member)
        {
            using var db = new ServerDbContext();
            if (await db.FamilyMembers.AnyAsync(m => m.CharacterID == member.CharacterID))
                db.FamilyMembers.Update(member);
            else
                db.FamilyMembers.Add(member);
            await db.SaveChangesAsync();
        }

        public static async Task RemoveMemberAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            await db.FamilyMembers.Where(m => m.CharacterID == characterID).ExecuteDeleteAsync();
        }

        public static async Task SetRelationAsync(uint clanID, uint targetID, byte kind)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO family_relation (FamilyID, TargetID, Kind) VALUES ({clanID}, {targetID}, {kind}) ON DUPLICATE KEY UPDATE Kind = VALUES(Kind)");
        }

        public static async Task RemoveRelationAsync(uint clanID, uint targetID)
        {
            using var db = new ServerDbContext();
            await db.FamilyRelations.Where(r => r.FamilyID == clanID && r.TargetID == targetID)
                .ExecuteDeleteAsync();
        }
    }
}
