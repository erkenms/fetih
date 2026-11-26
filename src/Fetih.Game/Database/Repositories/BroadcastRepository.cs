namespace Fetih.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class BroadcastRepository
    {
        public static async Task<List<DbBroadcast>> LoadAsync()
        {
            using var db = new ServerDbContext();
            return await db.Broadcasts.AsNoTracking().OrderBy(b => b.Position).ToListAsync();
        }

        public static async Task ReplaceAsync(List<DbBroadcast> rows)
        {
            using var db = new ServerDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM broadcast");
            db.Broadcasts.AddRange(rows);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
    }
}
