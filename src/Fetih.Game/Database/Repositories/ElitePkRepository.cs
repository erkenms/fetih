namespace Fetih.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ElitePkRepository
    {
        public static async Task<List<DbElitePkReward>> LoadAsync()
        {
            using var db = new ServerDbContext();
            return await db.ElitePkRewards.ToListAsync();
        }

        // A group's new placements replace its old ones, claimed or not
        public static async Task ReplaceGroupAsync(byte group, IEnumerable<DbElitePkReward> rows)
        {
            using var db = new ServerDbContext();
            await db.ElitePkRewards.Where(x => x.Group == group).ExecuteDeleteAsync();
            foreach (var row in rows)
            {
                await db.ElitePkRewards.Where(x => x.CharacterID == row.CharacterID).ExecuteDeleteAsync();
                db.ElitePkRewards.Add(row);
            }
            await db.SaveChangesAsync();
        }

        public static async Task ClearGroupAsync(byte group)
        {
            using var db = new ServerDbContext();
            await db.ElitePkRewards.Where(x => x.Group == group).ExecuteDeleteAsync();
        }

        public static async Task SetClaimedAsync(uint characterID, bool claimed)
        {
            using var db = new ServerDbContext();
            await db.ElitePkRewards.Where(x => x.CharacterID == characterID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Claimed, claimed));
        }
    }
}
