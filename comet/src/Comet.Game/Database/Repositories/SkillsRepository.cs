namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Magic;
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

        // Writes every skill as it is now; skills are never forgotten, so none are deleted
        public static async Task SaveAsync(uint characterID,
            (List<(ushort Type, SkillLevel Level)> Spells, List<(ushort Type, SkillLevel Level)> Proficiencies) skills)
        {
            if (skills.Spells.Count == 0 && skills.Proficiencies.Count == 0)
                return;
            using var db = new ServerDbContext();
            using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var (type, level) in skills.Spells)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO magic (CharacterID, Type, Level, Experience) VALUES ({characterID}, {type}, {level.Level}, {level.Experience}) ON DUPLICATE KEY UPDATE Level = VALUES(Level), Experience = VALUES(Experience)");
            foreach (var (type, level) in skills.Proficiencies)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO proficiency (CharacterID, Type, Level, Experience) VALUES ({characterID}, {type}, {level.Level}, {level.Experience}) ON DUPLICATE KEY UPDATE Level = VALUES(Level), Experience = VALUES(Experience)");
            await transaction.CommitAsync();
        }
    }
}
