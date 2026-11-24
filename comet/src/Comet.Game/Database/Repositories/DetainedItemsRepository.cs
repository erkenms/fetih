namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class DetainedItemsRepository
    {
        public static async Task CreateAsync(DbDetainedItem entry)
        {
            using var db = new ServerDbContext();
            db.DetainedItems.Add(entry);
            await db.SaveChangesAsync();
        }

        public static async Task<(DbDetainedItem Entry, DbItem Item)> FindAsync(uint detainID)
        {
            using var db = new ServerDbContext();
            var entry = await db.DetainedItems
                .Where(x => x.DetainID == detainID)
                .SingleOrDefaultAsync();
            if (entry == null)
                return (null, null);
            var item = entry.ItemID == 0
                ? null
                : await db.Items.Where(x => x.ItemID == entry.ItemID).SingleOrDefaultAsync();
            return (entry, item);
        }

        public static async Task<List<(DbDetainedItem Entry, DbItem Item)>> LoadForTargetAsync(uint targetID)
        {
            using var db = new ServerDbContext();
            var entries = await db.DetainedItems
                .Where(x => x.TargetID == targetID && !x.Redeemed)
                .OrderBy(x => x.DetainID)
                .ToListAsync();
            return await PairWithItemsAsync(db, entries);
        }

        public static async Task<List<(DbDetainedItem Entry, DbItem Item)>> LoadForHunterAsync(uint hunterID)
        {
            using var db = new ServerDbContext();
            var entries = await db.DetainedItems
                .Where(x => x.HunterID == hunterID)
                .OrderBy(x => x.DetainID)
                .ToListAsync();
            return await PairWithItemsAsync(db, entries);
        }

        public static async Task<bool> MarkRedeemedAsync(uint detainID)
        {
            using var db = new ServerDbContext();
            int affected = await db.DetainedItems
                .Where(x => x.DetainID == detainID && !x.Redeemed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Redeemed, true)
                    .SetProperty(x => x.ItemID, 0u));
            return affected > 0;
        }

        public static async Task<bool> DeleteAsync(uint detainID)
        {
            using var db = new ServerDbContext();
            int affected = await db.DetainedItems
                .Where(x => x.DetainID == detainID)
                .ExecuteDeleteAsync();
            return affected > 0;
        }

        private static async Task<List<(DbDetainedItem Entry, DbItem Item)>> PairWithItemsAsync(
            ServerDbContext db, List<DbDetainedItem> entries)
        {
            var itemIDs = entries.Where(e => !e.Redeemed && e.ItemID != 0).Select(e => e.ItemID).Distinct().ToList();
            var items = itemIDs.Count == 0
                ? new Dictionary<uint, DbItem>()
                : await db.Items.Where(i => itemIDs.Contains(i.ItemID)).ToDictionaryAsync(i => i.ItemID);
            return entries.Select(e => (e, items.TryGetValue(e.ItemID, out var item) ? item : null)).ToList();
        }
    }
}
