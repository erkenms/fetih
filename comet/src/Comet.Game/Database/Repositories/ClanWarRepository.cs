namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ClanWarRepository
    {
        public static async Task<(List<DbFamilyOccupy> Occupations, List<DbFamilyWarSignup> Signups)> LoadAsync()
        {
            using var db = new ServerDbContext();
            return (await db.FamilyOccupations.ToListAsync(), await db.FamilyWarSignups.ToListAsync());
        }

        public static async Task SaveOccupationAsync(DbFamilyOccupy row)
        {
            using var db = new ServerDbContext();
            if (await db.FamilyOccupations.AnyAsync(x => x.NpcID == row.NpcID))
                db.FamilyOccupations.Update(row);
            else
                db.FamilyOccupations.Add(row);
            await db.SaveChangesAsync();
        }

        public static async Task RemoveOccupationAsync(uint npcID)
        {
            using var db = new ServerDbContext();
            await db.FamilyOccupations.Where(x => x.NpcID == npcID).ExecuteDeleteAsync();
        }

        public static async Task SaveSignupAsync(DbFamilyWarSignup row)
        {
            using var db = new ServerDbContext();
            if (await db.FamilyWarSignups.AnyAsync(x => x.FamilyID == row.FamilyID))
                db.FamilyWarSignups.Update(row);
            else
                db.FamilyWarSignups.Add(row);
            await db.SaveChangesAsync();
        }

        public static async Task RemoveSignupAsync(uint clanID)
        {
            using var db = new ServerDbContext();
            await db.FamilyWarSignups.Where(x => x.FamilyID == clanID).ExecuteDeleteAsync();
        }

        public static async Task RemoveSignupsForAsync(IEnumerable<uint> npcIDs)
        {
            var ids = npcIDs.ToList();
            using var db = new ServerDbContext();
            await db.FamilyWarSignups.Where(x => ids.Contains(x.NpcID)).ExecuteDeleteAsync();
        }

        public static async Task RemoveClanAsync(uint clanID)
        {
            using var db = new ServerDbContext();
            await db.FamilyWarSignups.Where(x => x.FamilyID == clanID).ExecuteDeleteAsync();
            await db.FamilyOccupations.Where(x => x.FamilyID == clanID).ExecuteDeleteAsync();
        }
    }
}
