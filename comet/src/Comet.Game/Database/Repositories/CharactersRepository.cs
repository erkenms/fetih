namespace Comet.Game.Database.Repositories
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class CharactersRepository
    {
        public static async Task<DbCharacter> FindAsync(string name)
        {
            using var db = new ServerDbContext();
            return await db.Characters
                .Where(x => x.Name == name)
                .SingleOrDefaultAsync();
        }

        public static async Task<DbCharacter> FindAsync(uint accountID)
        {
            using var db = new ServerDbContext();
            return await db.Characters
                .Where(x => x.AccountID == accountID)
                .SingleOrDefaultAsync();
        }

        public static async Task<bool> ExistsAsync(string name)
        {
            using var db = new ServerDbContext();
            return await db.Characters
                .Where(x => x.Name == name)
                .AnyAsync();
        }

        public static async Task CreateAsync(DbCharacter character)
        {
            using var db = new ServerDbContext();
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        public static async Task SaveAsync(DbCharacter character)
        {
            using var db = new ServerDbContext();
            db.Characters.Update(character);
            await db.SaveChangesAsync();
        }

        public static async Task SetMarriageAsync(uint firstID, string firstSpouse, uint secondID, string secondSpouse)
        {
            using var db = new ServerDbContext();
            await db.Characters
                .Where(x => x.CharacterID == firstID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Spouse, firstSpouse));
            await db.Characters
                .Where(x => x.CharacterID == secondID)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Spouse, secondSpouse));
        }

        public static async Task ClearSpouseAsync(uint characterID, string spouseName, string none)
        {
            using var db = new ServerDbContext();
            await db.Characters
                .Where(x => x.CharacterID == characterID || x.Name == spouseName)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Spouse, none));
        }

        public static async Task DeleteAsync(uint characterID, string name, string none)
        {
            using var db = new ServerDbContext();
            using var tx = await db.Database.BeginTransactionAsync();
            var hunterDetainedItemIDs = await db.DetainedItems
                .Where(x => x.HunterID == characterID && !x.Redeemed && x.ItemID != 0)
                .Select(x => x.ItemID)
                .ToListAsync();
            if (hunterDetainedItemIDs.Count > 0)
                await db.Items.Where(x => hunterDetainedItemIDs.Contains(x.ItemID)).ExecuteDeleteAsync();
            await db.DetainedItems.Where(x => x.TargetID == characterID || x.HunterID == characterID).ExecuteDeleteAsync();
            await db.Items.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.Friends.Where(x => x.CharacterID == characterID || x.FriendID == characterID).ExecuteDeleteAsync();
            await db.Enemies.Where(x => x.CharacterID == characterID || x.EnemyID == characterID).ExecuteDeleteAsync();
            await db.TradePartners.Where(x => x.CharacterID == characterID || x.PartnerID == characterID).ExecuteDeleteAsync();
            await db.Magics.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.Proficiencies.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.SyndicateMembers.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.FamilyMembers.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.Arena.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.Nobility.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.Flowers.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.MessageBoard.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await db.Characters.Where(x => x.Spouse == name).ExecuteUpdateAsync(s => s.SetProperty(x => x.Spouse, none));
            await db.Characters.Where(x => x.CharacterID == characterID).ExecuteDeleteAsync();
            await tx.CommitAsync();
        }

        public static bool IsDuplicateName(DbUpdateException e) => IsDuplicateKey(e, "Name_UNIQUE");

        public static bool IsDuplicateAccount(DbUpdateException e) => IsDuplicateKey(e, "AccountID_UNIQUE");

        private static bool IsDuplicateKey(DbUpdateException e, string key) =>
            e.InnerException is MySqlConnector.MySqlException
            {
                ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry
            } inner && inner.Message.Contains($"'{key}'", StringComparison.OrdinalIgnoreCase);
    } 
}
