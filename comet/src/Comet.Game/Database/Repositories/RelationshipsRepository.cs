namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class RelationshipsRepository
    {
        // IDs and names of the character's friends and enemies
        public static async Task<(List<(uint ID, string Name)> Friends, List<(uint ID, string Name)> Enemies)>
            LoadAsync(uint characterID)
        {
            using var db = new ServerDbContext();
            var friends = await db.Friends.Where(f => f.CharacterID == characterID)
                .Join(db.Characters, f => f.FriendID, c => c.CharacterID, (f, c) => new { c.CharacterID, c.Name })
                .ToListAsync();
            var enemies = await db.Enemies.Where(e => e.CharacterID == characterID)
                .Join(db.Characters, e => e.EnemyID, c => c.CharacterID, (e, c) => new { c.CharacterID, c.Name })
                .ToListAsync();
            return (friends.Select(f => (f.CharacterID, f.Name)).ToList(),
                enemies.Select(e => (e.CharacterID, e.Name)).ToList());
        }

        public static async Task AddFriendsAsync(uint characterID, uint friendID)
        {
            using var db = new ServerDbContext();
            db.Friends.Add(new DbFriend { CharacterID = characterID, FriendID = friendID });
            db.Friends.Add(new DbFriend { CharacterID = friendID, FriendID = characterID });
            await db.SaveChangesAsync();
        }

        public static async Task RemoveFriendsAsync(uint characterID, uint friendID)
        {
            using var db = new ServerDbContext();
            await db.Friends.Where(f =>
                f.CharacterID == characterID && f.FriendID == friendID ||
                f.CharacterID == friendID && f.FriendID == characterID).ExecuteDeleteAsync();
        }

        public static async Task RemoveEnemyAsync(uint characterID, uint enemyID)
        {
            using var db = new ServerDbContext();
            await db.Enemies.Where(e => e.CharacterID == characterID && e.EnemyID == enemyID)
                .ExecuteDeleteAsync();
        }
    }
}
