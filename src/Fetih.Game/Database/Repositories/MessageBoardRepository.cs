namespace Fetih.Game.Database.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class MessageBoardRepository
    {
        public static async Task<List<DbMessageBoard>> LoadAsync()
        {
            using var db = new ServerDbContext();
            return await db.MessageBoard.AsNoTracking().ToListAsync();
        }

        public static async Task SaveAsync(ushort channel, uint characterID, uint syndicateID,
            string authorName, string message, DateTime postedAt)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO message_board (Channel, CharacterID, SyndicateID, AuthorName, Message, PostedAt)
                VALUES ({channel}, {characterID}, {syndicateID}, {authorName}, {message}, {postedAt})
                ON DUPLICATE KEY UPDATE
                    SyndicateID = VALUES(SyndicateID),
                    AuthorName = VALUES(AuthorName),
                    Message = VALUES(Message),
                    PostedAt = VALUES(PostedAt)");
        }

        public static async Task DeleteAsync(ushort channel, uint characterID)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM message_board WHERE Channel = {channel} AND CharacterID = {characterID}");
        }
    }
}
