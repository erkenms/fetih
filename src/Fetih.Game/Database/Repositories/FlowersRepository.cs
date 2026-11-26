namespace Fetih.Game.Database.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.Database.Models;
    using Microsoft.EntityFrameworkCore;

    public static class FlowersRepository
    {
        public sealed record Row(
            uint CharacterID,
            string Name,
            uint RedRoses,
            uint RedRosesToday,
            uint Lilies,
            uint LiliesToday,
            uint Orchids,
            uint OrchidsToday,
            uint Tulips,
            uint TulipsToday,
            DateTime? TodayDate);

        public static async Task<List<Row>> LoadAsync()
        {
            using var db = new ServerDbContext();
            var rows = await db.Flowers
                .Join(db.Characters, f => f.CharacterID, c => c.CharacterID,
                    (f, c) => new
                    {
                        f.CharacterID,
                        c.Name,
                        f.RedRoses,
                        f.RedRosesToday,
                        f.Lilies,
                        f.LiliesToday,
                        f.Orchids,
                        f.OrchidsToday,
                        f.Tulips,
                        f.TulipsToday,
                        f.TodayDate
                    })
                .ToListAsync();
            return rows.Select(x => new Row(
                x.CharacterID, x.Name,
                x.RedRoses, x.RedRosesToday,
                x.Lilies, x.LiliesToday,
                x.Orchids, x.OrchidsToday,
                x.Tulips, x.TulipsToday,
                x.TodayDate)).ToList();
        }

        public static async Task SaveAsync(DbFlower flower)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO flower (CharacterID, RedRoses, RedRosesToday, Lilies, LiliesToday, Orchids, OrchidsToday, Tulips, TulipsToday, TodayDate)
                VALUES ({flower.CharacterID}, {flower.RedRoses}, {flower.RedRosesToday}, {flower.Lilies}, {flower.LiliesToday},
                        {flower.Orchids}, {flower.OrchidsToday}, {flower.Tulips}, {flower.TulipsToday}, {flower.TodayDate})
                ON DUPLICATE KEY UPDATE
                    RedRoses = VALUES(RedRoses),
                    RedRosesToday = VALUES(RedRosesToday),
                    Lilies = VALUES(Lilies),
                    LiliesToday = VALUES(LiliesToday),
                    Orchids = VALUES(Orchids),
                    OrchidsToday = VALUES(OrchidsToday),
                    Tulips = VALUES(Tulips),
                    TulipsToday = VALUES(TulipsToday),
                    TodayDate = VALUES(TodayDate)");
        }

        public static async Task SaveSentDayAsync(uint characterID, DateTime day)
        {
            using var db = new ServerDbContext();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE `character` SET FlowerSentDay = {day.Date} WHERE CharacterID = {characterID}");
        }
    }
}
