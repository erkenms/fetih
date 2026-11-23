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

        public static bool IsDuplicateName(DbUpdateException e) => IsDuplicateKey(e, "Name_UNIQUE");

        public static bool IsDuplicateAccount(DbUpdateException e) => IsDuplicateKey(e, "AccountID_UNIQUE");

        private static bool IsDuplicateKey(DbUpdateException e, string key) =>
            e.InnerException is MySqlConnector.MySqlException
            {
                ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry
            } inner && inner.Message.Contains($"'{key}'", StringComparison.OrdinalIgnoreCase);
    } 
}
