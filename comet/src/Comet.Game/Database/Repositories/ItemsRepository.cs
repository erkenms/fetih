namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ItemsRepository
    {
        public static async Task<List<DbItem>> LoadAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            return await db.Items
                .Where(x => x.CharacterID == characterID)
                .OrderBy(x => x.ItemID)
                .ToListAsync();
        }

        // Sets the item's ItemID
        public static async Task CreateAsync(DbItem item)
        {
            using var db = new ServerDbContext();
            db.Items.Add(item);
            await db.SaveChangesAsync();
        }
    }
}
