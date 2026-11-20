namespace Comet.Game.Arena
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    // What the arena windows show of a player
    public sealed record Fighter(uint ID, string Name, uint Mesh, uint Level, uint Class, uint Rank,
        uint Points, uint WinsToday, uint LossesToday, uint WinsTotal, uint LossesTotal,
        uint HonorPoints, uint HonorTotal);

    // A player's arena points, wins and losses and honor, with their name, look, level and
    // class for the rankings (also of players offline). Changed under the qualifier's lock.
    public sealed class ArenaRecord
    {
        public const byte MinLevel = 40;

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

        // A new player gets points by level (5808); one who reaches level 40 without having
        // played gets them then
        public static uint StartingPoints(byte level) =>
            level < MinLevel ? 0u :
            level < 90 ? 1500u :
            level < 100 ? 2200u :
            level < 110 ? 2700u :
            level < 120 ? 3200u : 4000u;

        public static uint DayOf(DateTime now) => (uint)(now - DateTime.UnixEpoch).TotalDays;

        // Today's wins and losses start over each UTC day
        public void Roll(DateTime now)
        {
            uint today = DayOf(now);
            if (this.Stored.Day == today)
                return;
            this.Stored.Day = today;
            this.Stored.WinsToday = 0;
            this.Stored.LossesToday = 0;
        }

        public void GiveStartingPoints()
        {
            if (!this.EverPlayed && this.Stored.Points == 0)
                this.Stored.Points = StartingPoints(this.Level);
        }

        // The loser gives the winner 3% of their points (Albetros). Each side gets the honor
        // bonuses the match earned; the counts are of the bonuses, for the EXP balls.
        public static (uint Points, int WinnerBonuses, int LoserBonuses) Settle(
            ArenaRecord winner, ArenaRecord loser, DateTime now)
        {
            winner.Roll(now);
            loser.Roll(now);
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

        // Today's ranking (5808): players who fought today, by points, then wins, then fewest
        // losses
        public static List<ArenaRecord> RankToday(IEnumerable<ArenaRecord> records, DateTime now)
        {
            uint today = DayOf(now);
            return records.Where(r => r.Stored.Day == today && r.MatchesToday > 0)
                .OrderByDescending(r => r.Stored.Points)
                .ThenByDescending(r => r.Stored.WinsToday)
                .ThenBy(r => r.Stored.LossesToday)
                .ThenBy(r => r.ID)
                .ToList();
        }

        public static List<ArenaRecord> RankByHonor(IEnumerable<ArenaRecord> records) =>
            records.Where(r => r.Stored.HonorTotal > 0)
                .OrderByDescending(r => r.Stored.HonorTotal)
                .ThenBy(r => r.ID)
                .ToList();
    }
}
