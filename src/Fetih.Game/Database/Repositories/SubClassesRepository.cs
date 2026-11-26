namespace Fetih.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class SubClassesRepository
    {
        public static async Task<List<DbSubClass>> LoadAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            return await db.SubClasses
                .Where(x => x.CharacterID == characterID)
                .ToListAsync();
        }

        public static async Task SaveAsync(DbSubClass row)
        {
            using var db = new ServerDbContext();
            var existing = await db.SubClasses.FindAsync(row.CharacterID, row.SubClassID);
            if (existing == null)
                db.SubClasses.Add(row);
            else
            {
                existing.Level = row.Level;
                existing.Phase = row.Phase;
            }
            await db.SaveChangesAsync();
        }
    }
}
