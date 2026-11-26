namespace Fetih.Game.Database.Repositories
{
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Fetih.Game.Magic;
    using Microsoft.EntityFrameworkCore;

    public static class SkillsRepository
    {
        public static async Task<Skills> LoadAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            var spells = await db.Magics.Where(x => x.CharacterID == characterID).ToListAsync();
            var proficiencies = await db.Proficiencies.Where(x => x.CharacterID == characterID).ToListAsync();
            return new Skills(spells, proficiencies);
        }

        // Writes every skill as it is now, and deletes the spells forgotten
        public static async Task SaveAsync(uint characterID, SkillsSnapshot skills)
        {
            if (skills.Spells.Count == 0 && skills.Proficiencies.Count == 0 && skills.Forgotten.Count == 0)
                return;
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var type in skills.Forgotten)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM magic WHERE CharacterID = {characterID} AND Type = {type}");
            foreach (var (type, level) in skills.Spells)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO magic (CharacterID, Type, Level, Experience, OldLevel) VALUES ({characterID}, {type}, {level.Level}, {level.Experience}, {level.OldLevel}) ON DUPLICATE KEY UPDATE Level = VALUES(Level), Experience = VALUES(Experience), OldLevel = VALUES(OldLevel)");
            foreach (var (type, level) in skills.Proficiencies)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO proficiency (CharacterID, Type, Level, Experience) VALUES ({characterID}, {type}, {level.Level}, {level.Experience}) ON DUPLICATE KEY UPDATE Level = VALUES(Level), Experience = VALUES(Experience)");
            await transaction.CommitAsync();
        }
    }
}
