namespace Comet.Game.Database.Repositories
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class ArenaRepository
    {
        public sealed record Row(DbArena Record, string Name, byte Level, byte Class, uint Mesh);

        public static async Task<List<Row>> LoadAsync()
        {
            using var db = new ServerDbContext();
            var rows = await db.Arena
                .Join(db.Characters, a => a.CharacterID, c => c.CharacterID,
                    (a, c) => new { a, c.Name, c.Level, c.CurrentClass, c.Mesh, c.Avatar })
                .ToListAsync();
            return rows.Select(x => new Row(x.a, x.Name, x.Level, x.CurrentClass, x.Mesh + x.Avatar * 10000u)).ToList();
        }

        public static async Task SaveAsync(DbArena record)
        {
            using var db = new ServerDbContext();
            await SaveAsync(db, record);
        }

        public static async Task SaveManyAsync(IReadOnlyList<DbArena> records)
        {
            if (records.Count == 0)
                return;
            using var db = new ServerDbContext();
            foreach (var record in records)
                await SaveAsync(db, record);
        }

        public static async Task<(uint Season, List<DbArenaSeason> Entries)> LoadLastSeasonAsync()
        {
            using var db = new ServerDbContext();
            var latest = await db.ArenaSeasons.MaxAsync(s => (uint?)s.Season);
            if (!latest.HasValue)
                return (0, new List<DbArenaSeason>());
            var entries = await db.ArenaSeasons
                .Where(s => s.Season == latest.Value)
                .OrderBy(s => s.Rank)
                .ToListAsync();
            return (latest.Value, entries);
        }

        public static async Task SaveSeasonAsync(IReadOnlyList<DbArenaSeason> entries)
        {
            if (entries.Count == 0)
                return;
            using var db = new ServerDbContext();
            foreach (var entry in entries)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO arena_season (Season, `Rank`, CharacterID, Name, Mesh, Level, Class, Points, Wins, Losses)
                    VALUES ({entry.Season}, {entry.Rank}, {entry.CharacterID}, {entry.Name}, {entry.Mesh},
                        {entry.Level}, {entry.Class}, {entry.Points}, {entry.Wins}, {entry.Losses})
                    ON DUPLICATE KEY UPDATE CharacterID = VALUES(CharacterID), Name = VALUES(Name), Mesh = VALUES(Mesh),
                        Level = VALUES(Level), Class = VALUES(Class), Points = VALUES(Points),
                        Wins = VALUES(Wins), Losses = VALUES(Losses)");
        }

        private static Task SaveAsync(ServerDbContext db, DbArena record) =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO arena (CharacterID, Points, Day, WinsToday, LossesToday, WinsTotal, LossesTotal, HonorPoints, HonorTotal)
                VALUES ({record.CharacterID}, {record.Points}, {record.Day}, {record.WinsToday}, {record.LossesToday},
                    {record.WinsTotal}, {record.LossesTotal}, {record.HonorPoints}, {record.HonorTotal})
                ON DUPLICATE KEY UPDATE Points = VALUES(Points), Day = VALUES(Day), WinsToday = VALUES(WinsToday),
                    LossesToday = VALUES(LossesToday), WinsTotal = VALUES(WinsTotal), LossesTotal = VALUES(LossesTotal),
                    HonorPoints = VALUES(HonorPoints), HonorTotal = VALUES(HonorTotal)");
    }
}
