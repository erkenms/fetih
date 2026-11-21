namespace Comet.Game.Database.Repositories
{
    using System;
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

        public static async Task SetPositionsAsync(IReadOnlyCollection<(uint ItemID, byte Position)> moves)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var (itemID, position) in moves)
                await db.Items.Where(x => x.ItemID == itemID)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Position, position));
            await transaction.CommitAsync();
        }

        public static async Task SetOwnerAsync(uint itemID, uint characterID, byte position)
        {
            using var db = new ServerDbContext();
            await db.Items.Where(x => x.ItemID == itemID).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CharacterID, characterID)
                .SetProperty(x => x.Position, position));
        }

        // New owners for several items at once, into their inventories; all or none
        public static async Task TransferAsync(IReadOnlyCollection<(uint ItemID, uint CharacterID)> transfers)
        {
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var (itemID, characterID) in transfers)
                await db.Items.Where(x => x.ItemID == itemID).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.CharacterID, characterID)
                    .SetProperty(x => x.Position, (byte)0));
            await transaction.CommitAsync();
        }

        public static async Task SetStorageAsync(uint itemID, byte position, uint package)
        {
            using var db = new ServerDbContext();
            await db.Items.Where(x => x.ItemID == itemID).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Position, position)
                .SetProperty(x => x.Package, package));
        }

        public static async Task SetLockAsync(uint itemID, bool locked, DateTime? unlocksAt)
        {
            using var db = new ServerDbContext();
            await db.Items.Where(x => x.ItemID == itemID).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Locked, locked)
                .SetProperty(x => x.UnlocksAt, unlocksAt));
        }

        public static async Task SetAmountAsync(uint itemID, ushort amount)
        {
            using var db = new ServerDbContext();
            await db.Items.Where(x => x.ItemID == itemID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Amount, amount));
        }

        // An upgrade's type, durability and sockets
        public static async Task SetUpgradeAsync(DbItem item)
        {
            using var db = new ServerDbContext();
            await db.Items.Where(x => x.ItemID == item.ItemID).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.TypeID, item.TypeID)
                .SetProperty(x => x.Amount, item.Amount)
                .SetProperty(x => x.AmountLimit, item.AmountLimit)
                .SetProperty(x => x.Gem1, item.Gem1)
                .SetProperty(x => x.Gem2, item.Gem2));
        }

        public static async Task DeleteAsync(uint itemID)
        {
            using var db = new ServerDbContext();
            await db.Items.Where(x => x.ItemID == itemID).ExecuteDeleteAsync();
        }

        // Ground items don't outlive the server
        public static async Task<int> DeleteGroundAsync(byte groundPosition)
        {
            using var db = new ServerDbContext();
            return await db.Items.Where(x => x.Position == groundPosition).ExecuteDeleteAsync();
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
