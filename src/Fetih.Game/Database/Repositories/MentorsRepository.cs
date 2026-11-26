namespace Fetih.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class MentorsRepository
    {
        public static async Task<(List<DbMentor> Relations, List<DbMentorReward> Rewards)> LoadAllAsync()
        {
            using var db = new ServerDbContext();
            var relations = await db.Mentors.AsNoTracking().ToListAsync();
            var rewards = await db.MentorRewards.AsNoTracking().ToListAsync();
            return (relations, rewards);
        }

        public static async Task<DbCharacter> FindCharacterAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            return await db.Characters.AsNoTracking()
                .Where(c => c.CharacterID == characterID)
                .SingleOrDefaultAsync();
        }

        public static async Task AddRelationAsync(DbMentor relation)
        {
            using var db = new ServerDbContext();
            db.Mentors.Add(relation);
            await db.SaveChangesAsync();
        }

        public static async Task RemoveRelationAsync(uint apprenticeID)
        {
            using var db = new ServerDbContext();
            await db.Mentors.Where(x => x.ApprenticeID == apprenticeID).ExecuteDeleteAsync();
        }

        public static async Task SaveRelationContributionAsync(
            uint apprenticeID, uint experience, ushort blessing, ushort composition)
        {
            using var db = new ServerDbContext();
            await db.Mentors
                .Where(x => x.ApprenticeID == apprenticeID)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Experience, experience)
                    .SetProperty(x => x.Blessing, blessing)
                    .SetProperty(x => x.Composition, composition));
        }

        public static async Task SaveRewardAsync(DbMentorReward reward)
        {
            using var db = new ServerDbContext();
            var existing = await db.MentorRewards.SingleOrDefaultAsync(x => x.CharacterID == reward.CharacterID);
            if (existing == null)
            {
                db.MentorRewards.Add(reward);
            }
            else
            {
                existing.Experience = reward.Experience;
                existing.Blessing = reward.Blessing;
                existing.Composition = reward.Composition;
            }
            await db.SaveChangesAsync();
        }
    }
}
