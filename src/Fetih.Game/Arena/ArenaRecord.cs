namespace Fetih.Game.Arena
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Fetih.Game.Database.Models;

    // What the arena windows show of a player
    public sealed record Fighter(uint ID, string Name, uint Mesh, uint Level, uint Class, uint Rank,
        uint Points, uint WinsToday, uint LossesToday, uint WinsTotal, uint LossesTotal,
        uint HonorPoints, uint HonorTotal);

    // Name, look, level and class are kept for the rankings of players offline too. Changed
    // under the qualifier's lock.
    public sealed class ArenaRecord
    {
        public const byte MinLevel = 40;
        public const int SeasonTopCount = 10;

        // The client: playing 20 matches in a day, or winning 9, earns 500 honor and an EXP ball
        public const uint HonorBonus = 500;
        public const uint BonusMatches = 20, BonusWins = 9;

        public DbArena Stored { get; }
        public string Name { get; set; }
        public uint Mesh { get; set; }
        public byte Level { get; set; }
        public byte Class { get; set; }

        public ArenaRecord(DbArena stored) => this.Stored = stored;

        public uint ID => this.Stored.CharacterID;
        public uint MatchesToday => this.Stored.WinsToday + this.Stored.LossesToday;
        public bool EverPlayed => this.Stored.WinsTotal + this.Stored.LossesTotal > 0;

        // A new player gets points by level; one who reaches level 40 without having played
        // gets them then
        public static uint StartingPoints(byte level) =>
            level < MinLevel ? 0u :
            level < 90 ? 1500u :
            level < 100 ? 2200u :
            level < 110 ? 2700u :
            level < 120 ? 3200u : 4000u;

        public static uint DayOf(DateTime now) => (uint)(Clock.Today(now) - DateTime.UnixEpoch).TotalDays;

        // Today's wins, losses and arena points start over each day (season)
        public void Roll(DateTime now) => this.Roll(DayOf(now));

        public void Roll(uint day)
        {
            if (this.Stored.Day == day)
                return;
            this.Reset(day);
        }

        public void Reset(uint day)
        {
            this.Stored.Day = day;
            this.Stored.WinsToday = 0;
            this.Stored.LossesToday = 0;
            this.Stored.Points = StartingPoints(this.Level);
        }

        public void GiveStartingPoints()
        {
            if (!this.EverPlayed && this.Stored.Points == 0)
                this.Stored.Points = StartingPoints(this.Level);
        }

        // The loser gives the winner 3% of their points. Each side gets the honor
        // bonuses the match earned; the counts are of the bonuses, for the EXP balls.
        public static (uint Points, int WinnerBonuses, int LoserBonuses) Settle(
            ArenaRecord winner, ArenaRecord loser, DateTime now) =>
            Settle(winner, loser, DayOf(now));

        public static (uint Points, int WinnerBonuses, int LoserBonuses) Settle(
            ArenaRecord winner, ArenaRecord loser, uint day)
        {
            winner.Roll(day);
            loser.Roll(day);
            uint points = loser.Stored.Points * 3 / 100;
            loser.Stored.Points -= points;
            winner.Stored.Points += points;

            winner.Stored.WinsToday++;
            winner.Stored.WinsTotal++;
            loser.Stored.LossesToday++;
            loser.Stored.LossesTotal++;
            int winnerBonuses = (winner.MatchesToday == BonusMatches ? 1 : 0) +
                (winner.Stored.WinsToday == BonusWins ? 1 : 0);
            int loserBonuses = loser.MatchesToday == BonusMatches ? 1 : 0;
            winner.AddHonor(HonorBonus * (uint)winnerBonuses);
            loser.AddHonor(HonorBonus * (uint)loserBonuses);
            return (points, winnerBonuses, loserBonuses);
        }

        public void AddHonor(uint honor)
        {
            this.Stored.HonorPoints += honor;
            this.Stored.HonorTotal += honor;
        }

        public Fighter ToFighter(uint rank) => new Fighter(this.ID, this.Name, this.Mesh, this.Level, this.Class, rank,
            this.Stored.Points, this.Stored.WinsToday, this.Stored.LossesToday, this.Stored.WinsTotal,
            this.Stored.LossesTotal, this.Stored.HonorPoints, this.Stored.HonorTotal);

        // A copy to save, so a save never reads a record being changed
        public DbArena Snapshot() => new DbArena
        {
            CharacterID = this.Stored.CharacterID,
            Points = this.Stored.Points,
            Day = this.Stored.Day,
            WinsToday = this.Stored.WinsToday,
            LossesToday = this.Stored.LossesToday,
            WinsTotal = this.Stored.WinsTotal,
            LossesTotal = this.Stored.LossesTotal,
            HonorPoints = this.Stored.HonorPoints,
            HonorTotal = this.Stored.HonorTotal,
        };

        // Today's ranking: players who fought today, by points, then wins, then fewest losses
        public static List<ArenaRecord> RankToday(IEnumerable<ArenaRecord> records, DateTime now) =>
            RankDay(records, DayOf(now));

        public static List<ArenaRecord> RankDay(IEnumerable<ArenaRecord> records, uint day) =>
            records.Where(r => r.Stored.Day == day && r.MatchesToday > 0)
                .OrderByDescending(r => r.Stored.Points)
                .ThenByDescending(r => r.Stored.WinsToday)
                .ThenBy(r => r.Stored.LossesToday)
                .ThenBy(r => r.ID)
                .ToList();

        public static List<DbArenaSeason> TopTen(IEnumerable<ArenaRecord> records, uint day, uint season) =>
            RankDay(records, day)
                .Take(SeasonTopCount)
                .Select((r, i) => new DbArenaSeason
                {
                    Season = season,
                    Rank = (uint)(i + 1),
                    CharacterID = r.ID,
                    Name = r.Name ?? string.Empty,
                    Mesh = r.Mesh,
                    Level = r.Level,
                    Class = r.Class,
                    Points = r.Stored.Points,
                    Wins = r.Stored.WinsToday,
                    Losses = r.Stored.LossesToday,
                })
                .ToList();

        // A season's end: each rank of the ended day earns its Honor Points
        public static List<(ArenaRecord Record, int Rank, uint Honor)> AwardSeasonHonor(
            IEnumerable<ArenaRecord> records, uint day, HonorRewards rewards)
        {
            var awarded = new List<(ArenaRecord, int, uint)>();
            var ranked = RankDay(records, day);
            for (int i = 0; i < ranked.Count && i < rewards.Count; i++)
            {
                uint honor = rewards.For(i + 1);
                ranked[i].AddHonor(honor);
                awarded.Add((ranked[i], i + 1, honor));
            }
            return awarded;
        }

        public static List<ArenaRecord> RankByHonor(IEnumerable<ArenaRecord> records) =>
            records.Where(r => r.Stored.HonorTotal > 0)
                .OrderByDescending(r => r.Stored.HonorTotal)
                .ThenBy(r => r.ID)
                .ToList();
    }
}
